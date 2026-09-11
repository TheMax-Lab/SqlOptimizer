using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Domain.Rules;

namespace SqlOptimizer.Rules.Support;

/// <summary>
/// Shared helpers used by the deterministic optimization rules: statement
/// enumeration, column resolution against optional schema metadata, SQL
/// Server type family normalization and FROM-side qualifier collection.
/// Rules use these helpers instead of re-implementing AST traversal.
/// </summary>
public static class RuleUtilities
{
    /// <summary>
    /// Enumerates every SELECT statement in the tree (root statement, CTE
    /// queries, set-operation branches, scalar subqueries and derived
    /// tables) in deterministic pre-order.
    /// </summary>
    /// <param name="root">The parsed AST root.</param>
    public static IEnumerable<SelectStatement> EnumerateStatements(SqlNode root) =>
        SqlAstWalker.OfType<SelectStatement>(root);

    /// <summary>
    /// Resolves a column reference to schema metadata when available.
    /// Qualified columns are resolved through the alias map of the statement
    /// the column appears in; unqualified columns are searched across all
    /// known tables in schema order. Returns null when no metadata exists or
    /// the column cannot be resolved.
    /// </summary>
    /// <param name="context">The analysis context (schema may be null).</param>
    /// <param name="statement">The statement the column appears in.</param>
    /// <param name="column">The column reference to resolve.</param>
    public static DatabaseColumn? ResolveColumn(
        SqlAnalysisContext context,
        SelectStatement statement,
        ColumnExpression column)
    {
        if (context.Schema is null)
        {
            return null;
        }

        if (column.TableAlias is not null)
        {
            var aliases = TableReferenceFinder.GetTableAliases(statement);
            return aliases.TryGetValue(column.TableAlias, out var tableReference)
                ? context.FindTable(tableReference)?.FindColumn(column.Name)
                : null;
        }

        return context.Schema.Tables
            .Select(table => table.FindColumn(column.Name))
            .FirstOrDefault(found => found is not null);
    }

    /// <summary>
    /// True when the expression contains at least one column reference.
    /// </summary>
    /// <param name="expression">The expression to inspect.</param>
    public static bool ReferencesColumn(SqlExpression expression) =>
        SqlAstWalker.OfType<ColumnExpression>(expression).Any();

    /// <summary>
    /// True when the expression contains a scalar function or a cast.
    /// </summary>
    /// <param name="expression">The expression to inspect.</param>
    public static bool UsesFunctionOrCast(SqlExpression expression) =>
        SqlAstWalker.OfType<FunctionExpression>(expression).Any()
        || SqlAstWalker.OfType<CastExpression>(expression).Any();

    /// <summary>
    /// Normalizes a SQL Server data type declaration to a coarse type family
    /// (for example <c>varchar(100)</c> → <c>varchar</c>,
    /// <c>decimal(10,2)</c> → <c>decimal</c>), or the base name in lower
    /// case for unrecognized types.
    /// </summary>
    /// <param name="dataType">Type declaration text.</param>
    public static string? GetTypeFamily(string? dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType))
        {
            return null;
        }

        var type = dataType.Trim().Split('(')[0].Trim().ToLowerInvariant();
        return type switch
        {
            "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext"
                or "xml" or "binary" or "varbinary" or "image" or "timestamp" => "char",
            "int" or "bigint" or "smallint" or "tinyint" => "int",
            "decimal" or "numeric" or "money" or "smallmoney" => "decimal",
            "float" or "real" => "float",
            "bit" => "bit",
            "date" => "date",
            "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" or "time" or "offset" => "datetime",
            "uniqueidentifier" or "guid" => "uniqueidentifier",
            "sqlvariant" => "sqlvariant",
            _ => type
        };
    }

    /// <summary>
    /// Normalizes a type declaration for no-op comparison: inner whitespace
    /// removed and upper case (for example <c> nvarchar (100) </c> →
    /// <c>NVARCHAR(100)</c>).
    /// </summary>
    /// <param name="dataType">Type declaration text.</param>
    public static string NormalizeType(string? dataType) =>
        string.Join(
            string.Empty,
            (dataType ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();

    /// <summary>
    /// Collects the table qualifiers (alias when present, otherwise table
    /// name) declared by a FROM source, without crossing derived-table
    /// boundaries.
    /// </summary>
    /// <param name="source">The FROM source to inspect.</param>
    public static HashSet<string> CollectQualifiers(FromSource source)
    {
        var qualifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        switch (source)
        {
            case TableReference table:
                qualifiers.Add(table.Qualifier);
                break;
            case SubquerySource { Alias: not null } subquery:
                qualifiers.Add(subquery.Alias!);
                break;
            case JoinSource join:
                foreach (var left in CollectQualifiers(join.Left))
                {
                    qualifiers.Add(left);
                }

                foreach (var right in CollectQualifiers(join.Right))
                {
                    qualifiers.Add(right);
                }

                break;
        }

        return qualifiers;
    }

    /// <summary>
    /// Returns the base table reference when the FROM source is a single
    /// table, otherwise null (derived tables and join trees cannot be
    /// attributed to one table).
    /// </summary>
    /// <param name="source">The FROM source to inspect.</param>
    public static TableReference? GetSingleTable(FromSource? source) =>
        source is TableReference table ? table : null;
}