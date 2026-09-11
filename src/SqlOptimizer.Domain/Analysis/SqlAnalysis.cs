using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Common;

namespace SqlOptimizer.Domain.Analysis;

/// <summary>
/// The complete result of analyzing one SQL query: the AST, deterministic
/// scores, rule findings and structural statistics.
/// </summary>
public sealed class SqlAnalysis
{
    /// <summary>The analyzed SQL text.</summary>
    public required string Sql { get; init; }

    /// <summary>The dialect of the analyzed query.</summary>
    public required SqlDialect Dialect { get; init; }

    /// <summary>The parsed AST root.</summary>
    public required SelectStatement Ast { get; init; }

    /// <summary>Deterministic complexity score, 0 (simplest) to 100 (most complex).</summary>
    public int ComplexityScore { get; init; }

    /// <summary>Deterministic performance risk score, 0 (no risk found) to 100 (worst).</summary>
    public int PerformanceScore { get; init; }

    /// <summary>Findings produced by the rule engine.</summary>
    public IReadOnlyList<SqlFinding> Findings { get; init; } = [];

    /// <summary>Structural statistics of the query.</summary>
    public QueryStatistics Statistics { get; init; } = new();
}
