using SqlOptimizer.Domain.Optimization;
using SqlOptimizer.Domain.Rules;

namespace SqlOptimizer.Application.Abstractions;

/// <summary>
/// Heuristic index advisor. Recommendations are based on query structure and
/// optional schema metadata (never on workload statistics) and must be
/// reviewed before being created.
/// </summary>
public interface IIndexAdvisor
{
    /// <summary>
    /// Produces index recommendations for the given context, or an empty list.
    /// </summary>
    /// <param name="context">The analysis context.</param>
    IReadOnlyList<IndexRecommendation> Recommend(SqlAnalysisContext context);
}
