using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Domain.Optimization;

namespace SqlOptimizer.Application.DTOs;

/// <summary>
/// Request to optimize a SQL query (see the individual properties).
/// </summary>
public sealed record SqlOptimizationRequest
{
    /// <summary>The SQL query text.</summary>
    public required string Sql { get; init; }

    /// <summary>The SQL dialect (default: SqlServer).</summary>
    public SqlDialect Dialect { get; init; } = SqlDialect.SqlServer;

    /// <summary>Optional schema metadata.</summary>
    public DatabaseSchema? Schema { get; init; }

    /// <summary>Optional execution plan XML (SQL Server format).</summary>
    public string? ExecutionPlan { get; init; }

    /// <summary>Pipeline options.</summary>
    public OptimizationOptions Options { get; init; } = new();
}
