using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL020 — excessive scalar function usage in a query, using the
/// configurable <c>ExcessiveFunctionThreshold</c>. Aggregates are excluded
/// (they are expected with GROUP BY). This is a review flag: many short
/// built-in calls are harmless, so confidence is kept low and the finding
/// never claims a measured cost.
/// </summary>
public sealed class ExcessiveFunctionUsageRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL020";

    /// <inheritdoc />
    public override string Name => "Excessive function usage";

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        var functions = SqlAstWalker.OfType<FunctionExpression>(context.Ast).ToList();
        var threshold = context.Options.ExcessiveFunctionThreshold;

        if (functions.Count < threshold)
        {
            yield break;
        }

        var topFunctions = functions
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(3)
            .Select(group => $"{group.Count()}× {group.Key.ToUpperInvariant()}")
            .ToArray();

        yield return CreateFinding(
            Severity.Warning,
            FindingCategory.Performance,
            $"Query uses {functions.Count} scalar function calls (threshold: {threshold}). Most frequent: {string.Join(", ", topFunctions)}.",
            null,
            explanation: "A high number of function calls can increase per-row CPU cost, but many calls are harmless (short built-ins). The impact depends on the number of processed rows and the individual function cost, so this is a review flag, not a diagnosis. Repeated application of the same function to the same column is also reported by SQL002 (predicates) and SQL017 (duplicates).",
            recommendations: new[]
            {
                "Review whether the same function is applied repeatedly to the same column.",
                "For expensive per-row operations, consider computed columns or application-side logic.",
                "Verify the cost with an execution plan before optimizing."
            },
            confidence: 0.5,
            impact: new OptimizationImpact(Performance: 3, Readability: 0, Maintainability: 1, Risk: 1));
    }
}