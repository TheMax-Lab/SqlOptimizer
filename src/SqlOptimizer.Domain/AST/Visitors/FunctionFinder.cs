namespace SqlOptimizer.Domain.AST.Visitors;

/// <summary>
/// Helpers to locate function and aggregate usages in the AST.
/// </summary>
public static class FunctionFinder
{
    /// <summary>
    /// Enumerates every scalar function expression in the tree.
    /// </summary>
    /// <param name="root">Tree root.</param>
    public static IEnumerable<FunctionExpression> FindFunctions(SqlNode root) =>
        SqlAstWalker.OfType<FunctionExpression>(root);

    /// <summary>
    /// Enumerates scalar functions with the given name (case insensitive).
    /// </summary>
    /// <param name="root">Tree root.</param>
    /// <param name="name">Function name to match.</param>
    public static IEnumerable<FunctionExpression> FindFunctions(SqlNode root, string name) =>
        FindFunctions(root).Where(f =>
            string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Enumerates every aggregate expression in the tree.
    /// </summary>
    /// <param name="root">Tree root.</param>
    public static IEnumerable<AggregateExpression> FindAggregates(SqlNode root) =>
        SqlAstWalker.OfType<AggregateExpression>(root);

    /// <summary>
    /// Enumerates every CAST/CONVERT expression in the tree.
    /// </summary>
    /// <param name="root">Tree root.</param>
    public static IEnumerable<CastExpression> FindCasts(SqlNode root) =>
        SqlAstWalker.OfType<CastExpression>(root);

    /// <summary>
    /// True when the expression (or any sub-expression) invokes a scalar
    /// function or aggregate.
    /// </summary>
    /// <param name="expression">The expression to inspect.</param>
    public static bool UsesFunction(SqlExpression expression) =>
        SqlAstWalker.OfType<FunctionExpression>(expression).Any()
        || SqlAstWalker.OfType<AggregateExpression>(expression).Any();
}
