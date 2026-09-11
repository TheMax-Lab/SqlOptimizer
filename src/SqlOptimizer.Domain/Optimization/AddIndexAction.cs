namespace SqlOptimizer.Domain.Optimization;

/// <summary>
/// Add an index to support predicates or joins.
/// </summary>
/// <param name="Description">What the action would do.</param>
/// <param name="Reason">Why the action is proposed (ties back to a finding).</param>
/// <param name="Risk">Risk of applying the action.</param>
/// <param name="Confidence">0-1 confidence that the action is beneficial.</param>
/// <param name="Table">Table the index would be created on.</param>
/// <param name="KeyColumns">Proposed key columns in order.</param>
/// <param name="SqlFragment">SQL fragment the action targets, when available.</param>
/// <param name="RelatedRuleId">Identifier of the rule that produced the underlying finding.</param>
public sealed record AddIndexAction(
    string Description,
    string Reason,
    ActionRisk Risk,
    double Confidence,
    string Table,
    IReadOnlyList<string> KeyColumns,
    string? SqlFragment = null,
    string? RelatedRuleId = null)
    : OptimizationAction(Description, Reason, Risk, Confidence, SqlFragment, RelatedRuleId)
{
    /// <inheritdoc />
    public override string ActionType => nameof(AddIndexAction);
}
