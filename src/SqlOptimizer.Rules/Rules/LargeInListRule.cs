using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Support;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL015 — large literal IN lists, using the configurable
/// <c>LargeInListThreshold</c>. Large lists bloat the query and can degrade
/// the plan; table-based alternatives scale better. IN predicates with
/// subqueries are not reported here.
/// </summary>
public sealed class LargeInListRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL015";

    /// <inheritdoc />
    public override string Name => "Large IN list";

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        foreach (var statement in RuleUtilities.EnumerateStatements(context.Ast))
        {
            foreach (var inExpression in SqlAstWalker.DirectNodes(statement).OfType<InExpression>())
            {
                if (!inExpression.HasValueList ||
                    inExpression.Values.Count < context.Options.LargeInListThreshold)
                {
                    continue;
                }

                yield return CreateFinding(
                    Severity.Warning,
                    FindingCategory.Performance,
                    $"IN list with {inExpression.Values.Count} values (threshold: {context.Options.LargeInListThreshold}).",
                    $"{PredicatePositions.Describe(inExpression.Expression)} IN ({inExpression.Values.Count} values)",
                    explanation: "Large literal IN lists bloat the query text and can force the optimizer into a plan with many range seeks or a large in-memory structure; table-based alternatives often scale better. The actual impact depends on list size, index availability and selectivity, so this is a review flag rather than a diagnosis.",
                    recommendations: new[]
                    {
                        "For large or dynamic lists, use a temporary table or table-valued parameter and JOIN instead.",
                        "Check the execution plan: many range seeks may already have collapsed into a scan."
                    },
                    confidence: 0.7,
                    impact: new OptimizationImpact(Performance: 5, Readability: 1, Maintainability: 2, Risk: 1));
            }
        }
    }
}