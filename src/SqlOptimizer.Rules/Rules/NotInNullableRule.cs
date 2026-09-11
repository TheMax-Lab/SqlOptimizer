using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Support;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL008 — NOT IN with NULL risk. If a NOT IN source (subquery column or
/// literal list) can contain NULL, the predicate evaluates to UNKNOWN for
/// every row and matches nothing: a classic correctness trap. When schema
/// metadata proves the subquery column is NOT NULL the trap is not reported;
/// without metadata the rule reports it with reduced confidence.
/// </summary>
public sealed class NotInNullableRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL008";

    /// <inheritdoc />
    public override string Name => "NOT IN with NULL risk";

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        foreach (var statement in RuleUtilities.EnumerateStatements(context.Ast))
        {
            foreach (var inExpression in SqlAstWalker.DirectNodes(statement).OfType<InExpression>())
            {
                if (!inExpression.Not)
                {
                    continue;
                }

                if (inExpression.HasSubquery)
                {
                    if (TryGetSubqueryColumn(context, inExpression, out var columnDescription, out var nullable))
                    {
                        if (nullable == false)
                        {
                            // Proven NOT NULL: no NULL trap.
                            continue;
                        }

                        yield return CreateFinding(
                            nullable is true ? Severity.Critical : Severity.High,
                            FindingCategory.Correctness,
                            "NOT IN (SELECT ...) can match no rows when the subquery returns NULL.",
                            $"NOT IN ({columnDescription})",
                            explanation: nullable is true
                                ? "The subquery column is nullable according to the schema. If the subquery returns even one NULL, 'x NOT IN (... NULL ...)' evaluates to UNKNOWN for every row, so the predicate filters out all rows. This is a correctness bug, not a performance issue. A NOT EXISTS formulation or an explicit NULL filter on the subquery removes the trap."
                                : "Whether the subquery can return NULL cannot be proven from the available metadata. If it can, 'x NOT IN (... NULL ...)' evaluates to UNKNOWN for every row and the predicate matches nothing. Verify the subquery column's nullability before trusting the result.",
                            recommendations: new[]
                            {
                                "Prefer NOT EXISTS (SELECT 1 FROM ... WHERE key = outer.key), which is NULL-safe.",
                                "Or add an explicit NULL filter to the subquery (for example WHERE column IS NOT NULL).",
                                "If the column is known to be NOT NULL, consider documenting that constraint."
                            },
                            confidence: nullable is true ? 0.9 : 0.6,
                            impact: new OptimizationImpact(Performance: 1, Readability: 0, Maintainability: 2, Risk: 8));
                    }
                }
                else if (inExpression.HasValueList &&
                         inExpression.Values.Any(v => v is LiteralExpression { IsNull: true }))
                {
                    yield return CreateFinding(
                        Severity.Critical,
                        FindingCategory.Correctness,
                        "NOT IN list contains a NULL literal: the predicate is always UNKNOWN.",
                        PredicatePositions.Describe(inExpression),
                        explanation: "A NULL in a NOT IN value list makes the predicate evaluate to UNKNOWN for every row, so no row is ever returned. This is always wrong, regardless of data.",
                        recommendations: new[]
                        {
                            "Remove the NULL from the list (NULL is never a valid IN match).",
                            "Or express the intent explicitly with a separate IS NULL predicate."
                        },
                        confidence: 1.0,
                        impact: new OptimizationImpact(Performance: 0, Readability: 0, Maintainability: 2, Risk: 9));
                }
            }
        }
    }

    /// <summary>
    /// Resolves the first projected column of a NOT IN subquery and its
    /// nullability (null = unknown), using the shared analysis context.
    /// </summary>
    private static bool TryGetSubqueryColumn(
        SqlAnalysisContext context,
        InExpression inExpression,
        out string columnDescription,
        out bool? nullable)
    {
        columnDescription = "SELECT ...";
        nullable = null;

        var subquery = inExpression.Subquery;
        if (subquery is null)
        {
            return false;
        }

        var firstItem = subquery.Statement.SelectItems.FirstOrDefault();
        if (firstItem?.Expression is not ColumnExpression column)
        {
            return false;
        }

        columnDescription = PredicatePositions.Describe(column);

        var resolved = RuleUtilities.ResolveColumn(context, subquery.Statement, column);

        if (resolved is not null)
        {
            nullable = resolved.Nullable;
        }

        return true;
    }
}