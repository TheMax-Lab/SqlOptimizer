namespace SqlOptimizer.Domain.Optimization;

/// <summary>
/// Rewrite a join (for example LEFT JOIN with outer filter to INNER JOIN, or
/// removing a cross join) after semantic review.
/// </summary>
public sealed record RewriteJoinAction(
    string Description,
    string Reason,
    ActionRisk Risk,
    double Confidence,
    string? SqlFragment = null,
    string? RelatedRuleId = null) : OptimizationAction(Description, Reason, Risk, Confidence, SqlFragment, RelatedRuleId)
{
    /// <inheritdoc />
    public override string ActionType => nameof(RewriteJoinAction);
}
