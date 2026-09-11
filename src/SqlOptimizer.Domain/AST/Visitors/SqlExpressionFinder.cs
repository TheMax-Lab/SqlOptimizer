namespace SqlOptimizer.Domain.AST.Visitors;

/// <summary>
/// Helpers to locate expressions in the AST, including the predicate
/// positions (WHERE, HAVING, join ON) that matter for optimization rules.
/// </summary>
public static class SqlExpressionFinder
{
    /// <summary>
    /// Enumerates all expressions of the given type in the tree.
    /// </summary>
    /// <typeparam name="T">Expression type.</typeparam>
    /// <param name="root">Tree root.</param>
    public static IEnumerable<T> FindExpressions<T>(SqlNode root)
        where T : SqlExpression =>
        SqlAstWalker.OfType<T>(root);

    /// <summary>
    /// Enumerates the top-level predicate positions of a statement: WHERE,
    /// HAVING and every join ON predicate (including nested join levels),
    /// excluding subquery predicates.
    /// </summary>
    /// <param name="statement">The statement to inspect.</param>
    public static IEnumerable<SqlExpression> FindPredicateExpressions(SelectStatement statement)
    {
        if (statement.Where is not null)
        {
            yield return statement.Where;
        }

        if (statement.Having is not null)
        {
            yield return statement.Having;
        }

        if (statement.From is not null)
        {
            foreach (var join in JoinFinder.FindJoins(statement.From.Source))
            {
                if (join.Predicate is not null)
                {
                    yield return join.Predicate;
                }
            }
        }
    }

    /// <summary>
    /// Enumerates all comparison binary expressions anywhere in the tree.
    /// </summary>
    /// <param name="root">Tree root.</param>
    public static IEnumerable<BinaryExpression> FindComparisons(SqlNode root) =>
        SqlAstWalker.OfType<BinaryExpression>(root).Where(e => e.IsComparison);

    /// <summary>
    /// Enumerates all AND/OR boolean binary expressions anywhere in the tree.
    /// </summary>
    /// <param name="root">Tree root.</param>
    public static IEnumerable<BinaryExpression> FindBooleanExpressions(SqlNode root) =>
        SqlAstWalker.OfType<BinaryExpression>(root)
            .Where(e => e.Operator is SqlBinaryOperator.And or SqlBinaryOperator.Or);

    /// <summary>
    /// Enumerates the direct operands of the WHERE/HAVING/join predicates of a
    /// statement, splitting on AND/OR at any depth.
    /// </summary>
    /// <param name="statement">The statement to inspect.</param>
    public static IEnumerable<SqlExpression> FindAtomicPredicates(SelectStatement statement)
    {
        foreach (var predicate in FindPredicateExpressions(statement))
        {
            foreach (var atomic in SplitBoolean(predicate))
            {
                yield return atomic;
            }
        }
    }

    /// <summary>
    /// Recursively splits a boolean expression into its AND/OR leaves.
    /// </summary>
    /// <param name="expression">The boolean expression.</param>
    public static IEnumerable<SqlExpression> SplitPredicates(SqlExpression expression) =>
        SplitBoolean(expression);

    /// <summary>
    /// Recursively splits a boolean expression into its AND/OR leaves.
    /// </summary>
    /// <param name="expression">The boolean expression.</param>
    private static IEnumerable<SqlExpression> SplitBoolean(SqlExpression expression)
    {
        if (expression is BinaryExpression { Operator: SqlBinaryOperator.And or SqlBinaryOperator.Or } binary)
        {
            foreach (var left in SplitBoolean(binary.Left))
            {
                yield return left;
            }

            foreach (var right in SplitBoolean(binary.Right))
            {
                yield return right;
            }

            yield break;
        }

        yield return expression;
    }
}
