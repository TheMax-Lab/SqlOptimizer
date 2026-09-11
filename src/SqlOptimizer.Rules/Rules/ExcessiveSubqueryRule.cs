using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL014 — excessive subquery nesting, using the configurable
/// <c>MaxSubqueryDepth</c> threshold. Deep nesting is reported as a
/// maintainability/performance review flag, not as a proven cost.
/// </summary>
public sealed class ExcessiveSubqueryRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL014";

    /// <inheritdoc />
    public override string Name => "Excessive subquery nesting";

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        var maxDepth = SubqueryFinder.GetMaxDepth(context.Ast);
        if (maxDepth <= context.Options.MaxSubqueryDepth)
        {
            yield break;
        }

        yield return CreateFinding(
            Severity.Warning,
            FindingCategory.Performance,
            $"Subquery nesting depth is {maxDepth} (configured maximum: {context.Options.MaxSubqueryDepth}).",
            null,
            explanation: "Deeply nested subqueries are harder to read and maintain, and each nesting level can add a re-evaluation cost in the execution plan. Deep nesting is not always slow, but it is a common sign that the logic could be restructured into CTEs or joins.",
            recommendations: new[]
            {
                "Consider restructuring nested subqueries into CTEs or joins.",
                "Check the execution plan to see how each nesting level is executed."
            },
            confidence: 0.6,
            impact: new OptimizationImpact(Performance: 4, Readability: 3, Maintainability: 4, Risk: 2));
    }
}