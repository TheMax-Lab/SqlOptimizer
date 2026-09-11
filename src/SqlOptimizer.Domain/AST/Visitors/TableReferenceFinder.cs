namespace SqlOptimizer.Domain.AST.Visitors;

/// <summary>
/// Helpers to locate table references and resolve table aliases.
/// </summary>
public static class TableReferenceFinder
{
    /// <summary>
    /// Enumerates every base table reference in the tree (including
    /// subqueries and set-operation branches).
    /// </summary>
    /// <param name="root">Tree root.</param>
    public static IEnumerable<TableReference> FindTables(SqlNode root) =>
        SqlAstWalker.OfType<TableReference>(root);

    /// <summary>
    /// Maps every alias (or table name, when unaliased) declared in the FROM
    /// clause of a statement to its table.
    /// </summary>
    /// <param name="statement">The statement to inspect.</param>
    public static IReadOnlyDictionary<string, TableReference> GetTableAliases(SelectStatement statement)
    {
        var map = new Dictionary<string, TableReference>(StringComparer.OrdinalIgnoreCase);

        if (statement.From is null)
        {
            return map;
        }

        foreach (var table in Flatten(statement.From.Source))
        {
            map[table.Qualifier] = table;
        }

        return map;
    }

    /// <summary>
    /// Recursively flattens a FROM source into its base tables.
    /// </summary>
    /// <param name="source">The FROM source to flatten.</param>
    private static IEnumerable<TableReference> Flatten(FromSource source)
    {
        switch (source)
        {
            case TableReference table:
                yield return table;
                break;
            case SubquerySource subquery:
                foreach (var inner in FlattenOf(subquery.Query))
                {
                    yield return inner;
                }

                break;
            case JoinSource join:
                foreach (var left in Flatten(join.Left))
                {
                    yield return left;
                }

                foreach (var right in Flatten(join.Right))
                {
                    yield return right;
                }

                break;
        }
    }

    /// <summary>
    /// Collects base tables of a statement, including CTEs and set operations.
    /// </summary>
    /// <param name="statement">The statement.</param>
    private static IEnumerable<TableReference> FlattenOf(SelectStatement statement)
    {
        if (statement.From is not null)
        {
            foreach (var table in Flatten(statement.From.Source))
            {
                yield return table;
            }
        }

        foreach (var cte in statement.Ctes)
        {
            foreach (var table in FlattenOf(cte.Query))
            {
                yield return table;
            }
        }

        if (statement.SetOperation is not null)
        {
            foreach (var table in FlattenOf(statement.SetOperation.Right))
            {
                yield return table;
            }
        }
    }
}
