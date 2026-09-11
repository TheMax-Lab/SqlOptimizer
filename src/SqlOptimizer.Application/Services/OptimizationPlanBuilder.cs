using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Optimization;
using SqlOptimizer.Domain.Rules;

namespace SqlOptimizer.Application.Services;

/// <summary>
/// Deterministic optimization plan builder: maps rule findings to concrete
/// optimization action proposals, filtered by the requested strategy and
/// ordered by risk then confidence. Every action stays a proposal: none is
/// a guaranteed optimization and none is applied here. Rules without a safe
/// mechanical rewrite (for example SELECT * or UNION) intentionally produce
/// no action — only a recommendation.
/// </summary>
public sealed class OptimizationPlanBuilder : IOptimizationPlanBuilder
{
    /// <inheritdoc />
    public OptimizationPlan Build(SqlAnalysis analysis, SqlAnalysisContext context, OptimizationStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(context);

        var actions = new List<OptimizationAction>();
        foreach (var finding in analysis.Findings)
        {
            var action = CreateAction(finding);
            if (action is not null && IsAllowedByStrategy(action, strategy))
            {
                actions.Add(action);
            }
        }

        return new OptimizationPlan(
            actions
                .OrderBy(a => a.Risk)
                .ThenByDescending(a => a.Confidence)
                .ThenBy(a => a.ActionType, StringComparer.Ordinal)
                .ThenBy(a => a.RelatedRuleId, StringComparer.Ordinal)
                .ToList());
    }

    /// <summary>Maps a finding to the action it motivates, or null.</summary>
    private static OptimizationAction? CreateAction(SqlFinding finding)
    {
        ActionRisk risk;
        string action;

        switch (finding.RuleId)
        {
            case "SQL002": // function on a column
            case "SQL003": // non-sargable predicate
                risk = ActionRisk.Medium;
                action = nameof(RewritePredicateAction);
                break;

            case "SQL004": // leading wildcard LIKE
            case "SQL008": // NOT IN with nullable
            case "SQL012": // cartesian join
            case "SQL013": // LEFT JOIN with outer filter
            case "SQL014": // excessive subqueries
            case "SQL015": // large IN list
                risk = ActionRisk.High;
                action = finding.RuleId switch
                {
                    "SQL004" or "SQL015" => nameof(RewritePredicateAction),
                    "SQL008" or "SQL014" => nameof(ReplaceSubqueryAction),
                    _ => nameof(RewriteJoinAction)
                };
                break;

            case "SQL005": // implicit conversion
            case "SQL006": // OR predicate
                risk = ActionRisk.Medium;
                action = nameof(RewritePredicateAction);
                break;

            case "SQL007": // correlated subquery
                risk = ActionRisk.High;
                action = nameof(ReplaceSubqueryAction);
                break;

            case "SQL009": // DISTINCT
                risk = ActionRisk.High;
                action = nameof(RemoveDistinctAction);
                break;

            case "SQL011": // redundant ORDER BY
            case "SQL016": // unnecessary CAST
            case "SQL017": // duplicate expression
                risk = ActionRisk.Low;
                action = nameof(RemoveRedundantExpressionAction);
                break;

            case "SQL018": // potential join explosion
                risk = ActionRisk.Medium;
                action = nameof(RewriteJoinAction);
                break;

            case "SQL019": // missing join predicate
                risk = ActionRisk.High;
                action = nameof(RewriteJoinAction);
                break;

            default: // SQL001 (star projection) and SQL010 (UNION): recommendation only.
                return null;
        }

        var description = finding.Recommendations.FirstOrDefault() ?? finding.Message;
        var reason = finding.Explanation ?? finding.Message;

        return action switch
        {
            nameof(RewritePredicateAction) => new RewritePredicateAction(description, reason, risk, finding.Confidence, finding.SqlFragment, finding.RuleId),
            nameof(ReplaceSubqueryAction) => new ReplaceSubqueryAction(description, reason, risk, finding.Confidence, finding.SqlFragment, finding.RuleId),
            nameof(RewriteJoinAction) => new RewriteJoinAction(description, reason, risk, finding.Confidence, finding.SqlFragment, finding.RuleId),
            nameof(RemoveDistinctAction) => new RemoveDistinctAction(description, reason, risk, finding.Confidence, finding.SqlFragment, finding.RuleId),
            _ => new RemoveRedundantExpressionAction(description, reason, risk, finding.Confidence, finding.SqlFragment, finding.RuleId)
        };
    }

    /// <summary>Strategy gate: Conservative allows low risk only, Balanced low and medium.</summary>
    private static bool IsAllowedByStrategy(OptimizationAction action, OptimizationStrategy strategy) =>
        strategy switch
        {
            OptimizationStrategy.Conservative => action.Risk == ActionRisk.Low,
            OptimizationStrategy.Balanced => action.Risk is ActionRisk.Low or ActionRisk.Medium,
            _ => true
        };
}
