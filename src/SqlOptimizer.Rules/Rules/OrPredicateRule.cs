using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Support;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL006 — OR predicates in WHERE/HAVING. OR is reported as an
/// informational observation, never as a definite problem: SQL Server may
/// evaluate OR with a single scan or split it into seeks plus a union
/// (OR expansion); the outcome depends on selectivity, indexes and
/// statistics.
/// </summary>
public sealed class OrPredicateRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL006";

    /// <inheritdoc />
    public override string Name => "OR predicate";

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        foreach (var statement in RuleUtilities.EnumerateStatements(context.Ast))
        {
            var roots = new List<SqlExpression>();
            if (statement.Where is not null)
            {
                roots.Add(statement.Where);
            }

            if (statement.Having is not null)
            {
                roots.Add(statement.Having);
            }

            foreach (var root in roots)
            {
                foreach (var orNode in FindOrNodes(root))
                {
                    yield return CreateFinding(
                        Severity.Info,
                        FindingCategory.Performance,
                        "OR predicate in WHERE/HAVING.",
                        PredicatePositions.Describe(orNode),
                        explanation: "OR is not inherently slow: SQL Server may evaluate it with a single scan, or split it into separate index accesses combined as a union (OR expansion). Whether alternatives such as UNION ALL of the individual predicates help depends entirely on selectivity, indexes and statistics. UNION ALL is only safe when the two branches cannot return the same row; otherwise duplicate rows appear, which changes the result semantics.",
                        recommendations: new[]
                        {
                            "Check the execution plan to see whether the OR forces a scan.",
                            "If both branches are selective, consider a UNION ALL of the individual queries after verifying no duplicate rows are possible.",
                            "Consider separate indexes for each OR branch when appropriate."
                        },
                        confidence: 0.5,
                        impact: new OptimizationImpact(Performance: 2, Readability: 0, Maintainability: 0, Risk: 2));
                }
            }
        }
    }

    /// <summary>
    /// Enumerates OR nodes of a predicate tree: OR nodes themselves are
    /// reported and not descended into; AND nodes are descended through.
    /// </summary>
    /// <param name="expression">The predicate tree root.</param>
    private static IEnumerable<SqlExpression> FindOrNodes(SqlExpression expression)
    {
        if (expression is BinaryExpression { Operator: SqlBinaryOperator.Or } orNode)
        {
            yield return orNode;
            yield break;
        }

        if (expression is BinaryExpression { Operator: SqlBinaryOperator.And } andNode)
        {
            foreach (var left in FindOrNodes(andNode.Left))
            {
                yield return left;
            }

            foreach (var right in FindOrNodes(andNode.Right))
            {
                yield return right;
            }
        }
    }
}