using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Support;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL001 — star projection in a SELECT list (<c>*</c> or <c>t.*</c>).
/// Reports the maintainability and I/O risks of projecting all columns
/// without claiming the query is slow: the impact depends on result size,
/// schema width and index design.
/// </summary>
public sealed class SelectStarRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL001";

    /// <inheritdoc />
    public override string Name => "Select star projection";

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        foreach (var statement in RuleUtilities.EnumerateStatements(context.Ast))
        {
            var stars = statement.SelectItems.Where(item => item.IsStar).ToList();
            if (stars.Count == 0)
            {
                continue;
            }

            var fragment = string.Join(
                ", ",
                stars.Select(item => item.StarTableAlias is null ? "*" : $"{item.StarTableAlias}.*"));

            yield return CreateFinding(
                Severity.Warning,
                FindingCategory.Maintainability,
                $"SELECT list projects all columns ({fragment}).",
                fragment,
                explanation: "Projecting all columns can transfer and read more data than the query needs, can prevent the optimizer from choosing a narrow (covering) index, and makes the query contract fragile when the table schema changes. This is not always a performance problem: for small result sets or ad-hoc exploration a star projection may be perfectly fine.",
                recommendations: new[]
                {
                    "Project only the columns the query actually needs.",
                    "If all columns are genuinely needed, keep the star projection and document the schema dependency."
                },
                confidence: 0.9,
                impact: new OptimizationImpact(Performance: 2, Readability: 1, Maintainability: 5, Risk: 1));
        }
    }
}