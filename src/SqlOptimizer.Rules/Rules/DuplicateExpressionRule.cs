using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Support;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL017 — duplicate expressions: the same function call or CAST (root
/// level, normalized) appearing more than once in the same statement, for
/// example <c>expensive_function(x)</c> twice. Plain column references are
/// excluded on purpose: repeating a column across SELECT/GROUP BY/ORDER BY
/// is normal and flagging it would be noise.
/// </summary>
public sealed class DuplicateExpressionRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL017";

    /// <inheritdoc />
    public override string Name => "Duplicate expression";

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        foreach (var statement in RuleUtilities.EnumerateStatements(context.Ast))
        {
            var candidates = SqlAstWalker.DirectNodes(statement)
                .OfType<SqlExpression>()
                .Where(expression =>
                    expression is FunctionExpression { IsWindowed: false }
                    || expression is CastExpression)
                .ToList();

            var duplicates = candidates
                .GroupBy(ExpressionNormalizer.Normalize, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToList();

            foreach (var group in duplicates)
            {
                yield return CreateFinding(
                    Severity.Warning,
                    FindingCategory.Performance,
                    $"Expression '{group.Key}' is computed {group.Count()} times in the same statement.",
                    group.Key,
                    explanation: "The same function or conversion is evaluated repeatedly within one statement. SQL Server may or may not common-subexpression-eliminate it depending on the plan, so the repeated cost is not guaranteed either way. Materializing the expression once (CTE, derived table or computed column) makes the cost predictable and the query more readable.",
                    recommendations: new[]
                    {
                        "Compute the expression once in a CTE or derived table.",
                        "Consider a computed (persisted) column when the expression is used by many queries."
                    },
                    confidence: 0.6,
                    impact: new OptimizationImpact(Performance: 3, Readability: 2, Maintainability: 3, Risk: 1));
            }
        }
    }
}