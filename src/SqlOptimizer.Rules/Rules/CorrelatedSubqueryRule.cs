using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Support;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL007 — correlated subqueries: subqueries that reference columns of the
/// outer scope. Reported as a performance risk with an explicit caveat that
/// a correlated EXISTS is often already the optimal semi/anti-join shape; a
/// JOIN rewrite is never asserted as better.
/// </summary>
public sealed class CorrelatedSubqueryRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL007";

    /// <inheritdoc />
    public override string Name => "Correlated subquery";

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        foreach (var statement in RuleUtilities.EnumerateStatements(context.Ast))
        {
            var outerScope = SubqueryFinder.GetDirectScopeAliases(statement);
            if (outerScope.Count == 0)
            {
                continue;
            }

            foreach (var subquery in SubqueryFinder.FindDirectSubqueries(statement))
            {
                if (!SubqueryFinder.IsCorrelated(statement, subquery))
                {
                    continue;
                }

                var correlatedAliases = ColumnReferenceFinder
                    .FindColumns(subquery.Statement)
                    .Where(c => c.TableAlias is not null)
                    .Select(c => c.TableAlias!)
                    .Where(alias => outerScope.Contains(alias) &&
                        !SubqueryFinder.GetDeclaredAliases(subquery.Statement).Contains(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(alias => alias, StringComparer.Ordinal)
                    .ToList();

                yield return CreateFinding(
                    Severity.Warning,
                    FindingCategory.Performance,
                    $"Correlated subquery references outer scope ({string.Join(", ", correlatedAliases)}).",
                    "(subquery)",
                    explanation: "A correlated subquery re-executes for each row of the outer query. Whether this is a problem depends on the outer row count, the subquery selectivity and indexes; a correlated EXISTS is often already the optimal semi/anti-join shape, so a JOIN rewrite is not automatically better. Correlation is detected through qualified column references; unqualified references cannot be attributed to a scope and are not counted.",
                    recommendations: new[]
                    {
                        "Check the execution plan for repeated subquery execution (for example a Nested Loops with an outer reference).",
                        "Only consider a JOIN or anti-join rewrite if the plan shows repeated expensive work, and verify the semantics (duplicate rows, NULL behavior).",
                        "Ensure the outer columns referenced by the subquery are indexed in the inner query."
                    },
                    confidence: 0.8,
                    impact: new OptimizationImpact(Performance: 6, Readability: 1, Maintainability: 1, Risk: 3));
            }
        }
    }
}