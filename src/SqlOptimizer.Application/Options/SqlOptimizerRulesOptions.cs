namespace SqlOptimizer.Application.Options;

/// <summary>
/// Configurable rule thresholds.
/// </summary>
/// <param name="MaxSubqueryDepth">Maximum subquery depth before SQL014 fires.</param>
/// <param name="LargeInListThreshold">Minimum IN list size before SQL015 fires.</param>
/// <param name="ExcessiveFunctionThreshold">Minimum function count before SQL020 fires.</param>
public sealed record SqlOptimizerRulesOptions(
    int MaxSubqueryDepth = 3,
    int LargeInListThreshold = 20,
    int ExcessiveFunctionThreshold = 5);
