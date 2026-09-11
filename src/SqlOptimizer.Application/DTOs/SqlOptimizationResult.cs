using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Optimization;

namespace SqlOptimizer.Application.DTOs;

/// <summary>
/// Full result of the optimization pipeline.
/// </summary>
/// <param name="Analysis">The static analysis of the original query.</param>
/// <param name="OptimizationPlan">The deterministic optimization plan (proposals).</param>
/// <param name="Recommendations">Recommendations derived from findings.</param>
/// <param name="Candidates">Optimized SQL candidates, ranked best first; every candidate carries its own validation result and status.</param>
/// <param name="Indexes">Index recommendations.</param>
/// <param name="Prompt">Generated LLM prompt when requested.</param>
/// <param name="Validation">Validation result of the top-ranked candidate (null when no candidate was produced).</param>
public sealed record SqlOptimizationResult(
    SqlAnalysis Analysis,
    OptimizationPlan OptimizationPlan,
    IReadOnlyList<OptimizationRecommendation> Recommendations,
    IReadOnlyList<OptimizationCandidate> Candidates,
    IReadOnlyList<IndexRecommendation> Indexes,
    LlmOptimizationPrompt? Prompt,
    ValidationResult? Validation)
{
    /// <summary>Pipeline-level limitations (for example LLM unavailable, discarded duplicates).</summary>
    public IReadOnlyList<string> Limitations { get; init; } = [];
}
