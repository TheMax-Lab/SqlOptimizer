namespace SqlOptimizer.Domain.Optimization;

/// <summary>
/// Remove a redundant expression (for example a duplicated projection or a
/// redundant ORDER BY item) without changing the result.
/// </summary>
public sealed record RemoveRedundantExpressionAction(
    string Description,
    string Reason,
    ActionRisk Risk,
    double Confidence,
    string? SqlFragment = null,
    string? RelatedRuleId = null) : OptimizationAction(Description, Reason, Risk, Confidence, SqlFragment, RelatedRuleId)
{
    /// <inheritdoc />
    public override string ActionType => nameof(RemoveRedundantExpressionAction);
}
