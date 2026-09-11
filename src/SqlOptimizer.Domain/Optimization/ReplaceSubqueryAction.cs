namespace SqlOptimizer.Domain.Optimization;

/// <summary>
/// Replace a (correlated) subquery with a JOIN or CTE where semantics allow.
/// </summary>
public sealed record ReplaceSubqueryAction(
    string Description,
    string Reason,
    ActionRisk Risk,
    double Confidence,
    string? SqlFragment = null,
    string? RelatedRuleId = null) : OptimizationAction(Description, Reason, Risk, Confidence, SqlFragment, RelatedRuleId)
{
    /// <inheritdoc />
    public override string ActionType => nameof(ReplaceSubqueryAction);
}
