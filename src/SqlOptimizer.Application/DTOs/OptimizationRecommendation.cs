using SqlOptimizer.Domain.Analysis;

namespace SqlOptimizer.Application.DTOs;

/// <summary>
/// A recommendation derived from a finding. A recommendation is a suggestion,
/// never a guaranteed optimization.
/// </summary>
/// <param name="RuleId">Rule identifier.</param>
/// <param name="Message">Short description.</param>
/// <param name="Severity">Severity.</param>
/// <param name="Category">Category.</param>
/// <param name="SqlFragment">Related SQL fragment when available.</param>
/// <param name="SuggestedActions">Suggested actions.</param>
/// <param name="Confidence">Confidence that the finding applies, 0-1.</param>
/// <param name="Impact">Estimated impact dimensions.</param>
public sealed record OptimizationRecommendation(
    string RuleId,
    string Message,
    Severity Severity,
    FindingCategory Category,
    string? SqlFragment,
    IReadOnlyList<string> SuggestedActions,
    double Confidence,
    OptimizationImpact Impact);
