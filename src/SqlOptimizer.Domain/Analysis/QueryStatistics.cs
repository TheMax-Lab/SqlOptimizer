using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;

namespace SqlOptimizer.Domain.Analysis;

/// <summary>
/// Deterministic structural statistics of a query. All values are computed
/// from the AST; no randomness is involved.
/// </summary>
public sealed record QueryStatistics
{
    /// <summary>Number of base table references (including subqueries).</summary>
    public int TableCount { get; init; }

    /// <summary>Number of joins (including subqueries).</summary>
    public int JoinCount { get; init; }

    /// <summary>Number of subquery instances (scalar subqueries + derived tables).</summary>
    public int SubqueryCount { get; init; }

    /// <summary>Maximum subquery nesting depth (root statement = 0).</summary>
    public int MaxSubqueryDepth { get; init; }

    /// <summary>Number of predicate expressions in WHERE/HAVING/JOIN ON (all levels).</summary>
    public int PredicateCount { get; init; }

    /// <summary>Number of scalar function, aggregate and CAST usages (all levels).</summary>
    public int FunctionCount { get; init; }

    /// <summary>Number of aggregate usages (all levels).</summary>
    public int AggregateCount { get; init; }

    /// <summary>Largest literal IN list size (0 when none).</summary>
    public int InListMaxSize { get; init; }

    /// <summary>Number of items in the outer SELECT list.</summary>
    public int SelectColumnCount { get; init; }

    /// <summary>True when the outer SELECT uses DISTINCT.</summary>
    public bool HasDistinct { get; init; }

    /// <summary>True when the statement uses any set operation.</summary>
    public bool HasUnion { get; init; }

    /// <summary>Number of outer ORDER BY items.</summary>
    public int OrderByCount { get; init; }

    /// <summary>Number of CTEs declared by the statement.</summary>
    public int CteCount { get; init; }

    /// <summary>
    /// Computes statistics from a parsed statement.
    /// </summary>
    /// <param name="statement">The AST root.</param>
    public static QueryStatistics FromAst(SelectStatement statement) => new()
    {
        TableCount = SqlAstWalker.OfType<TableReference>(statement).Count(),
        JoinCount = SqlAstWalker.OfType<JoinSource>(statement).Count(),
        SubqueryCount = SubqueryFinder.CountSubqueries(statement),
        MaxSubqueryDepth = SubqueryFinder.GetMaxDepth(statement),
        PredicateCount = SqlAstWalker.OfType<BinaryExpression>(statement)
            .Count(b => b.Operator is
                SqlBinaryOperator.And or
                SqlBinaryOperator.Or or
                SqlBinaryOperator.Equal or
                SqlBinaryOperator.NotEqual or
                SqlBinaryOperator.GreaterThan or
                SqlBinaryOperator.GreaterThanOrEqual or
                SqlBinaryOperator.LessThan or
                SqlBinaryOperator.LessThanOrEqual),
        FunctionCount = SqlAstWalker.OfType<FunctionExpression>(statement).Count()
            + SqlAstWalker.OfType<AggregateExpression>(statement).Count()
            + SqlAstWalker.OfType<CastExpression>(statement).Count(),
        AggregateCount = SqlAstWalker.OfType<AggregateExpression>(statement).Count(),
        InListMaxSize = SqlAstWalker.OfType<InExpression>(statement)
            .Select(i => i.Values.Count)
            .DefaultIfEmpty(0)
            .Max(),
        SelectColumnCount = statement.SelectItems.Count,
        HasDistinct = statement.Distinct,
        HasUnion = SqlAstWalker.OfType<SetOperation>(statement).Any(),
        OrderByCount = statement.OrderBy.Count,
        CteCount = statement.Ctes.Count
    };
}
