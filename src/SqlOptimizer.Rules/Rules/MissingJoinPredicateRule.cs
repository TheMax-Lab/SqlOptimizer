using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Support;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL019 — missing join predicate: a non-CROSS join whose ON condition is
/// absent, constant (for example <c>ON 1 = 1</c>) or references only one
/// side of the join. Such joins behave like a Cartesian product and usually
/// indicate a missing join condition. Explicit CROSS JOINs are skipped
/// (that is deliberate; SQL012 reports them as performance issues).
/// Unqualified columns in the predicate make side attribution uncertain, so
/// the rule is conservative and skips those.
/// </summary>
public sealed class MissingJoinPredicateRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL019";

    /// <inheritdoc />
    public override string Name => "Missing join predicate";

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        foreach (var join in JoinFinder.FindJoins(context.Ast))
        {
            if (join.Type == JoinType.Cross)
            {
                continue;
            }

            if (join.Predicate is null)
            {
                yield return CreateFinding(
                    Severity.Critical,
                    FindingCategory.Correctness,
                    $"{join.Type} JOIN has no join predicate.",
                    Describe(join),
                    explanation: "An outer or inner join without an ON condition combines every row of both sides. This is almost always a missing join condition; the result set can be orders of magnitude larger than intended.",
                    recommendations: new[]
                    {
                        "Add the missing join condition between the two sides.",
                        "If the Cartesian product is intentional, use CROSS JOIN explicitly."
                    },
                    confidence: 0.85,
                    impact: new OptimizationImpact(Performance: 8, Readability: 2, Maintainability: 3, Risk: 8));
                continue;
            }

            if (JoinSource.IsConstantPredicate(join.Predicate))
            {
                yield return CreateFinding(
                    Severity.Critical,
                    FindingCategory.Correctness,
                    $"{join.Type} JOIN uses a constant predicate (ON {ExpressionNormalizer.Normalize(join.Predicate)}).",
                    Describe(join),
                    explanation: "A constant ON predicate does not relate the rows of the two sides, so the join produces a Cartesian product. This is usually a missing or placeholder join condition.",
                    recommendations: new[]
                    {
                        "Replace the constant predicate with the real join condition.",
                        "If the Cartesian product is intentional, use CROSS JOIN explicitly."
                    },
                    confidence: 0.8,
                    impact: new OptimizationImpact(Performance: 8, Readability: 2, Maintainability: 3, Risk: 8));
                continue;
            }

            var columns = ColumnReferenceFinder.FindColumns(join.Predicate).ToList();
            if (columns.Count == 0 || columns.Any(c => c.TableAlias is null))
            {
                // Without fully qualified references the predicate cannot be
                // attributed to a side; skip to avoid false positives.
                continue;
            }

            var leftQualifiers = RuleUtilities.CollectQualifiers(join.Left);
            var rightQualifiers = RuleUtilities.CollectQualifiers(join.Right);

            var referencesLeft = columns.Any(c => leftQualifiers.Contains(c.TableAlias!));
            var referencesRight = columns.Any(c => rightQualifiers.Contains(c.TableAlias!));

            if (referencesLeft && referencesRight)
            {
                continue;
            }

            yield return CreateFinding(
                Severity.Critical,
                FindingCategory.Correctness,
                "JOIN predicate does not relate both sides of the join.",
                Describe(join),
                explanation: "The ON condition references only one side of the join (or columns that do not belong to either side), so the join behaves like a Cartesian product with a filter. This usually means the join condition was forgotten.",
                recommendations: new[]
                {
                    "Add the missing join condition between the two sides.",
                    "If the Cartesian product is intentional, use CROSS JOIN explicitly."
                },
                confidence: 0.7,
                impact: new OptimizationImpact(Performance: 8, Readability: 2, Maintainability: 3, Risk: 8));
        }
    }

    /// <summary>Describes a join for finding fragments.</summary>
    /// <param name="join">The join to describe.</param>
    private static string Describe(JoinSource join) =>
        $"{DescribeSource(join.Left)} {join.Type} JOIN {DescribeSource(join.Right)}";

    /// <summary>Describes a FROM source briefly.</summary>
    /// <param name="source">The source to describe.</param>
    private static string DescribeSource(FromSource source) => source switch
    {
        TableReference table => table.Alias is null ? table.Name : $"{table.Name} AS {table.Alias}",
        SubquerySource => "(derived table)",
        _ => "(join)"
    };
}