using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Domain.Optimization;

namespace SqlOptimizer.Application.DTOs;

/// <summary>
/// Everything the prompt generator needs beyond the analysis result.
/// </summary>
/// <param name="OriginalSql">The original SQL text.</param>
/// <param name="Dialect">The SQL dialect.</param>
/// <param name="Analysis">The static analysis result (findings, statistics, scores).</param>
/// <param name="Schema">Schema metadata when available.</param>
/// <param name="ExecutionPlanXml">Raw execution plan XML when available.</param>
/// <param name="Options">The optimization options in effect.</param>
/// <param name="Plan">The deterministic optimization plan.</param>
/// <param name="IndexRecommendations">Index recommendations.</param>
public sealed record SqlOptimizationContext(
    string OriginalSql,
    SqlDialect Dialect,
    SqlAnalysis Analysis,
    DatabaseSchema? Schema,
    string? ExecutionPlanXml,
    OptimizationOptions Options,
    OptimizationPlan Plan,
    IReadOnlyList<IndexRecommendation> IndexRecommendations);
