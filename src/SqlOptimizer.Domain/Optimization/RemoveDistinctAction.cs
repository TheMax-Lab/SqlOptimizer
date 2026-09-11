namespace SqlOptimizer.Domain.Optimization;

/// <summary>
/// Remove a DISTINCT that may be unnecessary (only proposed when duplicates
/// are unlikely; always high risk because it can change result cardinality).
/// </summary>
public sealed record RemoveDistinctAction(
    string Description,
    string Reason,
    ActionRisk Risk,
    double Confidence,
    string? SqlFragment = null,
    string? RelatedRuleId = null) : OptimizationAction(Description, Reason, Risk, Confidence, SqlFragment, RelatedRuleId)
{
    /// <inheritdoc />
    public override string ActionType => nameof(RemoveDistinctAction);
}
