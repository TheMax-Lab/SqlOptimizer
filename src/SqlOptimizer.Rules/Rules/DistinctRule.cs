using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Support;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL009 — SELECT DISTINCT. Reports the de-duplication cost and the
/// possibility that DISTINCT hides duplicate row production upstream (for
/// example join fan-out). DISTINCT is never recommended for automatic
/// removal.
/// </summary>
public sealed class DistinctRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL009";

    /// <inheritdoc />
    public override string Name => "SELECT DISTINCT";

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        foreach (var statement in RuleUtilities.EnumerateStatements(context.Ast))
        {
            if (!statement.Distinct)
            {
                continue;
            }

            var hasJoins = statement.From is not null
                && JoinFinder.FindJoins(statement.From.Source, includeSubqueries: false).Any();

            yield return CreateFinding(
                Severity.Info,
                FindingCategory.Performance,
                "SELECT DISTINCT forces de-duplication of the result.",
                "SELECT DISTINCT",
                explanation: "DISTINCT requires a de-duplication step (sort or hash) over the full result set. It can also hide duplicate row production upstream, for example fan-out from a join: if the source already produces unique rows, DISTINCT is redundant work; if it does not, DISTINCT is masking the real problem. Removing DISTINCT is not safe unless the source is proven to produce unique rows.",
                recommendations: new[]
                {
                    "Check whether duplicates come from a join or repeated logic, and fix the source if so.",
                    "If the de-duplication cost shows in the execution plan, consider restructuring the query instead of relying on DISTINCT."
                },
                confidence: hasJoins ? 0.7 : 0.5,
                impact: new OptimizationImpact(Performance: 3, Readability: 0, Maintainability: 1, Risk: 2));
        }
    }
}