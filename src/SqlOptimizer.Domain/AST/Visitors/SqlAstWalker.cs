namespace SqlOptimizer.Domain.AST.Visitors;

/// <summary>
/// Static helpers to walk the AST deterministically (pre-order depth first).
/// </summary>
public static class SqlAstWalker
{
    /// <summary>
    /// Enumerates every node in the tree, pre-order depth first, starting from
    /// (and including) the root.
    /// </summary>
    /// <param name="root">Tree root.</param>
    public static IEnumerable<SqlNode> AllNodes(SqlNode root)
    {
        yield return root;

        foreach (var child in root.Children)
        {
            foreach (var descendant in AllNodes(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Enumerates every node of the given type.
    /// </summary>
    /// <typeparam name="T">Node type filter.</typeparam>
    /// <param name="root">Tree root.</param>
    public static IEnumerable<T> OfType<T>(SqlNode root)
        where T : SqlNode =>
        AllNodes(root).OfType<T>();

    /// <summary>
    /// Enumerates every node matching the predicate.
    /// </summary>
    /// <param name="root">Tree root.</param>
    /// <param name="predicate">Node filter.</param>
    public static IEnumerable<SqlNode> Find(SqlNode root, Func<SqlNode, bool> predicate) =>
        AllNodes(root).Where(predicate);

    /// <summary>
    /// Returns the first node matching the predicate, or null.
    /// </summary>
    /// <param name="root">Tree root.</param>
    /// <param name="predicate">Node filter.</param>
    public static SqlNode? FindFirst(SqlNode root, Func<SqlNode, bool> predicate) =>
        AllNodes(root).FirstOrDefault(predicate);

    /// <summary>
    /// Counts nodes matching the predicate.
    /// </summary>
    /// <param name="root">Tree root.</param>
    /// <param name="predicate">Node filter.</param>
    public static int Count(SqlNode root, Func<SqlNode, bool> predicate) =>
        AllNodes(root).Count(predicate);

    /// <summary>
    /// Enumerates the nodes of a tree without crossing subquery boundaries
    /// (scalar subqueries and derived tables). The boundary nodes themselves
    /// are yielded; the statements they contain are not visited.
    /// </summary>
    /// <param name="root">Tree root.</param>
    public static IEnumerable<SqlNode> DirectNodes(SqlNode root)
    {
        var stack = new Stack<SqlNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            if (node is SubqueryExpression or SubquerySource)
            {
                continue;
            }

            foreach (var child in node.Children)
            {
                stack.Push(child);
            }
        }
    }
}
