using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Support;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL012 — Cartesian products: explicit CROSS JOINs (including comma FROM
/// lists, which the parser maps to CROSS JOINs) and joins without a
/// meaningful ON predicate (for example <c>ON 1 = 1</c>). Reported as a
/// performance risk; a Cartesian product is occasionally intentional, so the
/// finding always says so.
/// </summary>
public sealed class CartesianJoinRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL012";

    /// <inheritdoc />
    public override string Name => "Cartesian join";

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        foreach (var join in JoinFinder.FindJoins(context.Ast))
        {
            var joinText = Describe(join);

            if (join.Type == JoinType.Cross)
            {
                yield return CreateFinding(
                    Severity.High,
                    FindingCategory.Performance,
                    "CROSS JOIN produces the Cartesian product of both sides.",
                    joinText,
                    explanation: "CROSS JOIN combines every row of the left side with every row of the right side, multiplying the row count (leftRows × rightRows). It is occasionally intentional (for example a small dimension table), but it is also the most common accidental row explosion. If a relationship between the tables is missing, the join condition should be added (see SQL019).",
                    recommendations: new[]
                    {
                        "Check that the Cartesian product is intentional.",
                        "If a relationship is missing, add the join condition.",
                        "Aggregate or filter one side before the join when the full product is not needed."
                    },
                    confidence: 0.7,
                    impact: new OptimizationImpact(Performance: 8, Readability: 0, Maintainability: 1, Risk: 3));
                continue;
            }

            if (!join.HasMeaningfulPredicate)
            {
                yield return CreateFinding(
                    Severity.High,
                    FindingCategory.Performance,
                    $"{join.Type} JOIN without a meaningful ON predicate behaves like a Cartesian product.",
                    joinText,
                    explanation: "A constant ON predicate (for example 1 = 1) or the absence of one does not relate the joined tables, so every combination of rows is produced. The optimizer treats this as a Cartesian product with an (always true) filter.",
                    recommendations: new[]
                    {
                        "Add the missing join condition.",
                        "If the product is intentional, use CROSS JOIN to make that explicit."
                    },
                    confidence: 0.85,
                    impact: new OptimizationImpact(Performance: 8, Readability: 0, Maintainability: 1, Risk: 3));
            }
        }
    }

    /// <summary>Describes a join for finding fragments.</summary>
    /// <param name="join">The join to describe.</param>
    private static string Describe(JoinSource join) =>
        $"{DescribeSource(join.Left)} {FormatType(join.Type)} {DescribeSource(join.Right)}" +
        (join.Predicate is null ? string.Empty : $" ON {ExpressionNormalizer.Normalize(join.Predicate)}");

    /// <summary>Describes a FROM source briefly.</summary>
    /// <param name="source">The source to describe.</param>
    private static string DescribeSource(FromSource source) => source switch
    {
        TableReference table => table.Alias is null ? table.Name : $"{table.Name} AS {table.Alias}",
        SubquerySource => "(derived table)",
        _ => "(join)"
    };

    /// <summary>Formats a join type as SQL text.</summary>
    /// <param name="type">The join type.</param>
    private static string FormatType(JoinType type) => type switch
    {
        JoinType.Inner => "JOIN",
        JoinType.Left => "LEFT JOIN",
        JoinType.Right => "RIGHT JOIN",
        JoinType.Full => "FULL JOIN",
        JoinType.Cross => "CROSS JOIN",
        _ => type.ToString()
    };
}