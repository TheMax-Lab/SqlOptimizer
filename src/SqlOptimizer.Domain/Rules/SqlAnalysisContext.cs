using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;

namespace SqlOptimizer.Domain.Rules;

/// <summary>
/// Everything a rule can inspect. The AST is parsed exactly once per
/// analysis and shared by every rule through this context.
/// </summary>
public sealed class SqlAnalysisContext
{
    /// <summary>The parsed AST root.</summary>
    public required SelectStatement Ast { get; init; }

    /// <summary>The dialect of the query.</summary>
    public required SqlDialect Dialect { get; init; }

    /// <summary>Schema metadata when available.</summary>
    public DatabaseSchema? Schema { get; init; }

    /// <summary>Execution plan when available.</summary>
    public ExecutionPlan? ExecutionPlan { get; init; }

    /// <summary>Configurable rule thresholds.</summary>
    public RuleOptions Options { get; init; } = new();

    /// <summary>
    /// Resolves a table reference against the schema metadata, or null when
    /// no schema is available or the table is unknown.
    /// </summary>
    /// <param name="table">The table reference from the AST.</param>
    public DatabaseTable? FindTable(TableReference table) =>
        Schema?.FindTable(table);

    /// <summary>
    /// Resolves the schema metadata for the base tables of a statement.
    /// </summary>
    /// <param name="statement">The statement to resolve.</param>
    public IReadOnlyList<DatabaseTable> ResolveTables(SelectStatement statement)
    {
        var tables = new List<DatabaseTable>();

        foreach (var reference in TableReferenceFinder.FindTables(statement))
        {
            var table = FindTable(reference);
            if (table is not null)
            {
                tables.Add(table);
            }
        }

        return tables.DistinctBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
