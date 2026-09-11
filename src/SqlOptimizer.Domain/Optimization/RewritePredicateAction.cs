namespace SqlOptimizer.Domain.Optimization;

/// <summary>
/// Rewrite a predicate to a potentially more sargable / simpler form.
/// </summary>
public sealed record RewritePredicateAction(
    string Description,
    string Reason,
    ActionRisk Risk,
    double Confidence,
    string? SqlFragment = null,
    string? RelatedRuleId = null) : OptimizationAction(Description, Reason, Risk, Confidence, SqlFragment, RelatedRuleId)
{
    /// <inheritdoc />
    public override string ActionType => nameof(RewritePredicateAction);
}
