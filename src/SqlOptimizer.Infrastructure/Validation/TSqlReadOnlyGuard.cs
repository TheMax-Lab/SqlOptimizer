using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Parsing;
using Sdom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlOptimizer.Infrastructure.Validation;

/// <summary>
/// Fail-closed, parser/AST-based read-only gate for SQL that is about to be
/// executed against a validation database. A statement is accepted only when
/// the T-SQL parser proves it is a single SELECT with no state-changing or
/// externally effecting constructs: DML, DDL, EXEC, transactions and control
/// flow are separate statement types the parser rejects up front, and the
/// guard adds the SELECT-level checks the parser alone cannot express
/// (SELECT INTO, OPENROWSET/OPENDATASOURCE, table-valued functions and
/// linked-server references). Anything that cannot be proven read-only is
/// rejected; safety never relies on regex or on prompt instructions.
/// </summary>
public static class TSqlReadOnlyGuard
{
    /// <summary>Outcome of the read-only check.</summary>
    /// <param name="IsReadOnly">True when the statement is provably read-only.</param>
    /// <param name="Reason">Rejection reason when <see cref="IsReadOnly"/> is false.</param>
    /// <param name="Statement">The parsed statement when the check passed.</param>
    public sealed record GuardOutcome(bool IsReadOnly, string? Reason, SelectStatement? Statement)
    {
        /// <summary>Accepts a statement that passed every check.</summary>
        internal static GuardOutcome Accept(SelectStatement statement) => new(true, null, statement);

        /// <summary>Rejects a statement with an explicit reason.</summary>
        internal static GuardOutcome Reject(string reason) => new(false, reason, null);
    }

    /// <summary>
    /// Checks that the SQL is a single, provably read-only SELECT statement.
    /// Never throws for SQL problems: failures are reported through the
    /// returned outcome.
    /// </summary>
    /// <param name="sql">The SQL text to check (untrusted).</param>
    /// <param name="parser">The dialect parser (must support the same dialect).</param>
    public static GuardOutcome Check(string sql, ISqlParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        if (string.IsNullOrWhiteSpace(sql))
        {
            return GuardOutcome.Reject("The SQL text is empty.");
        }

        SelectStatement statement;
        try
        {
            statement = parser.Parse(sql).Root;
        }
        catch (SqlInvalidInputException ex)
        {
            return GuardOutcome.Reject(ex.Message);
        }
        catch (SqlParseException ex)
        {
            return GuardOutcome.Reject(ex.Message);
        }

        var violation = FindSelectLevelViolation(sql);
        return violation is null
            ? GuardOutcome.Accept(statement)
            : GuardOutcome.Reject(violation);
    }

    /// <summary>
    /// Re-parses the statement with ScriptDom and traverses every node
    /// looking for the SELECT-level constructs the domain AST does not model
    /// (SELECT INTO, linked-server/cross-database references, table
    /// variables). Returns the first violation found, or null when the
    /// statement stays within the read-only surface.
    /// </summary>
    private static string? FindSelectLevelViolation(string sql)
    {
        var tsqlParser = Sdom.TSqlParser.CreateParser(Sdom.SqlVersion.Sql160, false);
        Sdom.TSqlFragment fragment;
        try
        {
            fragment = tsqlParser.Parse(new StringReader(sql), out _);
        }
        catch (Exception)
        {
            return "The statement could not be parsed by the T-SQL parser.";
        }

        if (fragment is not Sdom.TSqlScript script)
        {
            return "The statement is not a complete T-SQL script.";
        }

        var statements = script.Batches.SelectMany(batch => batch.Statements).ToList();
        if (statements.Count != 1 || statements[0] is not Sdom.SelectStatement)
        {
            return "The statement is not a single SELECT statement.";
        }

        var walker = new ReadOnlyStatementWalker();
        walker.Walk(script);
        return walker.Violation;
    }

    /// <summary>
    /// Built-in table functions that are guaranteed read-only by SQL Server
    /// (they cannot execute user code or open external connections). Any
    /// table function outside this list is rejected, fail-closed.
    /// </summary>
    private static readonly HashSet<string> ReadOnlyBuiltInTableFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "STRING_SPLIT",
        "OPENJSON",
        "FILETABLE",
        "OPENFILE",
        "GENERATE_SERIES",
    };

    /// <summary>
    /// Collects the first read-only violation found in the statement. The
    /// traversal is deliberately manual and null-safe: it does not rely on
    /// the ScriptDom fragment walker, whose generic child dispatch is not
    /// guaranteed to tolerate every node shape the dialect parser produces.
    /// </summary>
    private sealed class ReadOnlyStatementWalker
    {
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> FragmentProperties = new();

        /// <summary>The first violation found, or null.</summary>
        public string? Violation { get; private set; }

        /// <summary>Depth-first, null-safe traversal of every fragment node.</summary>
        public void Walk(Sdom.TSqlFragment root) => VisitNode(root);

        private void VisitNode(Sdom.TSqlFragment node)
        {
            if (Violation is not null)
            {
                return;
            }

            switch (node)
            {
                case Sdom.SelectStatement statement when statement.Into is not null:
                    Violation = "SELECT INTO creates a table and is not read-only.";
                    return;

                case Sdom.NamedTableReference reference
                    when reference.SchemaObject is { } schema
                        && (schema.ServerIdentifier is not null || schema.DatabaseIdentifier is not null):
                    Violation = "Linked-server or cross-database table references cannot be proven read-only within the validation database.";
                    return;

                case Sdom.VariableTableReference:
                    Violation = "Table variables are session-scoped; their contents cannot be provided for comparison.";
                    return;

                case Sdom.VariableMethodCallTableReference:
                    Violation = "Table variable method calls cannot be proven read-only.";
                    return;

                case Sdom.SchemaObjectFunctionTableReference:
                    Violation = "User-defined table-valued functions cannot be proven read-only.";
                    return;

                case Sdom.GlobalFunctionTableReference globalFunction
                    when !IsKnownReadOnlyBuiltIn(globalFunction.Name?.Value):
                    Violation = $"Table function '{globalFunction.Name?.Value}' cannot be proven read-only.";
                    return;

                case Sdom.BuiltInFunctionTableReference builtInFunction
                    when !IsKnownReadOnlyBuiltIn(builtInFunction.Name?.Value):
                    Violation = $"Table function '{builtInFunction.Name?.Value}' cannot be proven read-only.";
                    return;

                case Sdom.OpenRowsetTableReference:
                    Violation = "OPENROWSET queries an external data source and cannot be proven read-only.";
                    return;
            }

            foreach (var child in EnumerateChildFragments(node))
            {
                VisitNode(child);
                if (Violation is not null)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Yields every child fragment of the node, reading both direct
        /// fragment properties and generic collection properties of
        /// fragments (ScriptDom models children both ways).
        /// </summary>
        private static IEnumerable<Sdom.TSqlFragment> EnumerateChildFragments(Sdom.TSqlFragment node)
        {
            foreach (var property in FragmentProperties.GetOrAdd(
                         node.GetType(),
                         static type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)))
            {
                // Indexer properties take parameters and cannot be read without them.
                if (property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                var propertyType = property.PropertyType;
                if (typeof(Sdom.TSqlFragment).IsAssignableFrom(propertyType))
                {
                    if (property.GetValue(node) is Sdom.TSqlFragment child)
                    {
                        yield return child;
                    }
                }
                else if (propertyType.IsGenericType
                    && propertyType.GetGenericArguments() is { Length: 1 } arguments
                    && typeof(Sdom.TSqlFragment).IsAssignableFrom(arguments[0]))
                {
                    if (property.GetValue(node) is System.Collections.IEnumerable list)
                    {
                        foreach (var item in list)
                        {
                            if (item is Sdom.TSqlFragment listItem)
                            {
                                yield return listItem;
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>True when the name is a known read-only built-in table function.</summary>
    private static bool IsKnownReadOnlyBuiltIn(string? name) =>
        name is not null && ReadOnlyBuiltInTableFunctions.Contains(name);
}