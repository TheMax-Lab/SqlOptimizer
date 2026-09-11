using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Support;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL013 — WHERE/HAVING filter on the nullable side of an outer join
/// (for example <c>LEFT JOIN Orders o ... WHERE o.Status = 'Paid'</c>).
/// Such a predicate excludes the unmatched (all-NULL) rows, so the join
/// effectively behaves like an INNER JOIN. The rule explains the semantics
/// and both intended behaviors; it never rewrites the query.
/// </summary>
public sealed class LeftJoinFilterRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL013";

    /// <inheritdoc />
    public override string Name => "Outer join filtered in WHERE";

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        foreach (var statement in RuleUtilities.EnumerateStatements(context.Ast))
        {
            if (statement.From is null || statement.Where is null)
            {
                continue;
            }

            var atomicPredicates = SqlExpressionFinder
                .SplitPredicates(statement.Where)
                .ToList();

            foreach (var join in JoinFinder.FindJoins(statement.From.Source, includeSubqueries: false))
            {
                foreach (var (side, joinDescription) in NullableSides(join))
                {
                    var qualifiers = RuleUtilities.CollectQualifiers(side);
                    if (qualifiers.Count == 0)
                    {
                        continue;
                    }

                    var filteredColumns = atomicPredicates
                        .Where(p => ReferencesNullableSide(p, qualifiers))
                        .Select(p => ExpressionNormalizer.Normalize(p))
                        .Distinct(StringComparer.Ordinal)
                        .ToList();

                    if (filteredColumns.Count == 0)
                    {
                        continue;
                    }

                    yield return CreateFinding(
                        Severity.Warning,
                        FindingCategory.Join,
                        $"WHERE predicate filters the outer-joined side ({joinDescription}).",
                        string.Join(" AND ", filteredColumns),
                        explanation: "With an outer join, rows without a match have NULL in the joined table's columns, so a predicate like \"o.Status = 'Paid'\" excludes exactly those unmatched rows: the join effectively behaves like an INNER JOIN. If that is the intent, writing INNER JOIN is clearer and lets the optimizer know the semantics directly. If unmatched rows must be preserved, the filter must explicitly allow NULL (for example \"o.Status = 'Paid' OR o.Status IS NULL\"). Which behavior is correct is a business decision, so no automatic rewrite is applied.",
                        recommendations: new[]
                        {
                            "If only matched rows are wanted, change the outer join to an INNER JOIN.",
                            "If unmatched rows must survive, add an explicit NULL-allowing condition (for example OR o.x IS NULL)."
                        },
                        confidence: 0.8,
                        impact: new OptimizationImpact(Performance: 4, Readability: 3, Maintainability: 3, Risk: 4));
                }
            }
        }
    }

    /// <summary>
    /// Yields the nullable side(s) of an outer join with a description.
    /// LEFT: right side; RIGHT: left side; FULL: both sides.
    /// </summary>
    private static IEnumerable<(FromSource Side, string Description)> NullableSides(JoinSource join)
    {
        switch (join.Type)
        {
            case JoinType.Left:
                yield return (join.Right, "LEFT JOIN right side");
                break;

            case JoinType.Right:
                yield return (join.Left, "RIGHT JOIN left side");
                break;

            case JoinType.Full:
                yield return (join.Left, "FULL JOIN left side");
                yield return (join.Right, "FULL JOIN right side");
                break;
        }
    }

    /// <summary>
    /// True when an atomic predicate is a comparison (or IS NOT NULL) on a
    /// qualified column of the nullable side. Plain IS NULL predicates are
    /// legitimate outer-join filters and are not reported.
    /// </summary>
    private static bool ReferencesNullableSide(SqlExpression predicate, HashSet<string> qualifiers)
    {
        if (predicate is not BinaryExpression binary
            || binary.Operator == SqlBinaryOperator.Is
            || (!binary.IsComparison && binary.Operator != SqlBinaryOperator.IsNot))
        {
            return false;
        }

        foreach (var side in new[] { binary.Left, binary.Right })
        {
            if (side is ColumnExpression { TableAlias: not null } column
                && qualifiers.Contains(column.TableAlias))
            {
                return true;
            }
        }

        return false;
    }
}