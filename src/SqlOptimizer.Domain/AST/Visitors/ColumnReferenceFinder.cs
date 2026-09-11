namespace SqlOptimizer.Domain.AST.Visitors;

/// <summary>
/// Helpers to locate column references in the AST.
/// </summary>
public static class ColumnReferenceFinder
{
    /// <summary>
    /// Enumerates every column reference in the tree.
    /// </summary>
    /// <param name="root">Tree root.</param>
    public static IEnumerable<ColumnExpression> FindColumns(SqlNode root) =>
        SqlAstWalker.OfType<ColumnExpression>(root);

    /// <summary>
    /// Enumerates column references qualified with the given table alias
    /// (case insensitive).
    /// </summary>
    /// <param name="root">Tree root.</param>
    /// <param name="tableAlias">Table alias to match.</param>
    public static IEnumerable<ColumnExpression> FindColumns(SqlNode root, string tableAlias) =>
        FindColumns(root).Where(c =>
            string.Equals(c.TableAlias, tableAlias, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Enumerates column references by (case insensitive) column name,
    /// qualified or not.
    /// </summary>
    /// <param name="root">Tree root.</param>
    /// <param name="columnName">Column name to match.</param>
    public static IEnumerable<ColumnExpression> FindColumnsByName(SqlNode root, string columnName) =>
        FindColumns(root).Where(c =>
            string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Enumerates unqualified column references (no table alias).
    /// </summary>
    /// <param name="root">Tree root.</param>
    public static IEnumerable<ColumnExpression> FindUnqualifiedColumns(SqlNode root) =>
        FindColumns(root).Where(c => c.TableAlias is null);
}
