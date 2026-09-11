namespace SqlOptimizer.Domain.Analysis;

/// <summary>
/// Estimated impact of applying a finding's recommendation. All dimensions
/// are 0-10 heuristics (0 = no impact, 10 = maximum impact).
/// </summary>
/// <param name="Performance">Expected performance improvement.</param>
/// <param name="Readability">Expected readability improvement.</param>
/// <param name="Maintainability">Expected maintainability improvement.</param>
/// <param name="Risk">Risk of changing behavior (higher = riskier).</param>
public sealed record OptimizationImpact(
    int Performance,
    int Readability,
    int Maintainability,
    int Risk);
