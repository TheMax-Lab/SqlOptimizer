using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;

namespace SqlOptimizer.Application.DTOs;

/// <summary>
/// Request to analyze a SQL query (see the individual properties).
/// </summary>
public sealed record SqlAnalysisRequest
{
    /// <summary>The SQL query text.</summary>
    public required string Sql { get; init; }

    /// <summary>The SQL dialect (default: SqlServer).</summary>
    public SqlDialect Dialect { get; init; } = SqlDialect.SqlServer;

    /// <summary>Optional schema metadata for richer analysis.</summary>
    public DatabaseSchema? Schema { get; init; }

    /// <summary>Optional execution plan XML (SQL Server format).</summary>
    public string? ExecutionPlan { get; init; }

    /// <summary>Include the parsed AST in the API response.</summary>
    public bool IncludeAst { get; init; }
}
