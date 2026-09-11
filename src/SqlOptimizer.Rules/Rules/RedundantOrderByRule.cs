using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Support;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL011 — duplicate ORDER BY items. Only exact duplicates (same
/// normalized expression and direction) are reported; ORDER BY is never
/// recommended for removal in any other case.
/// </summary>
public sealed class RedundantOrderByRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL011";

    /// <inheritdoc />
    public override string Name => "Redundant ORDER BY item";

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        foreach (var statement in RuleUtilities.EnumerateStatements(context.Ast))
        {
            if (statement.OrderBy.Count < 2)
            {
                continue;
            }

            var duplicates = statement.OrderBy
                .GroupBy(item => ExpressionNormalizer.Normalize(item), StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .ToList();

            foreach (var group in duplicates)
            {
                yield return CreateFinding(
                    Severity.Info,
                    FindingCategory.Maintainability,
                    $"ORDER BY item '{group.Key}' appears {group.Count()} times.",
                    group.Key,
                    explanation: "Identical ORDER BY items have no effect beyond the first occurrence. Removing the duplicates changes nothing semantically; the remaining ordering must of course be kept — ORDER BY should never be dropped just because it looks redundant.",
                    recommendations: new[]
                    {
                        "Remove the duplicate ORDER BY items.",
                        "Keep the remaining ordering exactly as is."
                    },
                    confidence: 0.9,
                    impact: new OptimizationImpact(Performance: 0, Readability: 3, Maintainability: 3, Risk: 0));
            }
        }
    }
}