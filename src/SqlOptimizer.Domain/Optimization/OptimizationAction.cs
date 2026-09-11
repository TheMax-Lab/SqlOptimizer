namespace SqlOptimizer.Domain.Optimization;

/// <summary>
/// Base type for a concrete step in an <see cref="OptimizationPlan"/>.
/// An action is a *proposal* derived from a finding: it is never a
/// guaranteed optimization and must be validated before being applied.
/// </summary>
/// <param name="Description">What the action would do.</param>
/// <param name="Reason">Why the action is proposed (ties back to a finding).</param>
/// <param name="Risk">Risk of applying the action.</param>
/// <param name="Confidence">0-1 confidence that the action is beneficial.</param>
/// <param name="SqlFragment">SQL fragment the action targets, when available.</param>
/// <param name="RelatedRuleId">Identifier of the rule that produced the underlying finding.</param>
public abstract record OptimizationAction(
    string Description,
    string Reason,
    ActionRisk Risk,
    double Confidence,
    string? SqlFragment = null,
    string? RelatedRuleId = null)
{
    /// <summary>Stable machine readable action type name.</summary>
    public abstract string ActionType { get; }
}
