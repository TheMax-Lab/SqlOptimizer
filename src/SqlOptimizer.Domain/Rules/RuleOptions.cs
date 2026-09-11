using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;

namespace SqlOptimizer.Domain.Rules;

/// <summary>
/// Options for rule thresholds that must stay configurable.
/// </summary>
/// <param name="MaxSubqueryDepth">Maximum subquery nesting depth before SQL014 fires.</param>
/// <param name="LargeInListThreshold">Minimum literal IN list size before SQL015 fires.</param>
/// <param name="ExcessiveFunctionThreshold">Minimum scalar function usage count before SQL020 fires.</param>
public sealed record RuleOptions(
    int MaxSubqueryDepth = 3,
    int LargeInListThreshold = 20,
    int ExcessiveFunctionThreshold = 5);
