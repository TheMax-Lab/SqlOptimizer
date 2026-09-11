namespace SqlOptimizer.Domain.Optimization;

/// <summary>
/// A deterministic, ordered set of proposed optimization actions derived
/// from analysis findings. Actions are proposals: applying them requires
/// semantic review and, when enabled, runtime validation.
/// </summary>
/// <param name="Actions">Proposed actions, ordered by expected value.</param>
public sealed record OptimizationPlan(IReadOnlyList<OptimizationAction> Actions)
{
    /// <summary>
    /// Deterministic estimate (0-100) of the total potential benefit of the
    /// plan, discounted by each action's risk.
    /// </summary>
    public int EstimatedTotalBenefit
    {
        get
        {
            var total = Actions.Sum(action =>
            {
                var riskFactor = action.Risk switch
                {
                    ActionRisk.Low => 1.0,
                    ActionRisk.Medium => 0.8,
                    _ => 0.6
                };

                return (int)Math.Round(Math.Clamp(action.Confidence, 0d, 1d) * 100 * riskFactor);
            });

            return (int)Math.Clamp(total, 0, 100);
        }
    }
}
