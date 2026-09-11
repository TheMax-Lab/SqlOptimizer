namespace SqlOptimizer.Domain.AST.Visitors;

/// <summary>
/// Helpers to locate subqueries, measure nesting depth and detect
/// correlation with an outer scope.
/// </summary>
public static class SubqueryFinder
{
    /// <summary>
    /// Enumerates every scalar subquery expression in the tree.
    /// </summary>
    /// <param name="root">Tree root.</param>
    public static IEnumerable<SubqueryExpression> FindSubqueries(SqlNode root) =>
        SqlAstWalker.OfType<SubqueryExpression>(root);

    /// <summary>
    /// Counts all subquery instances (scalar subqueries and derived tables)
    /// of a statement, at any nesting level.
    /// </summary>
    /// <param name="statement">The statement to inspect.</param>
    public static int CountSubqueries(SelectStatement statement) =>
        SqlAstWalker.OfType<SubqueryExpression>(statement).Count()
        + SqlAstWalker.OfType<SubquerySource>(statement).Count();

    /// <summary>
    /// Computes the maximum subquery nesting depth of a statement. The root
    /// statement has depth 0; a subquery directly inside it has depth 1.
    /// </summary>
    /// <param name="statement">The root statement.</param>
    public static int GetMaxDepth(SelectStatement statement)
    {
        var max = 0;

        foreach (var subquery in SqlAstWalker.OfType<SubqueryExpression>(statement))
        {
            max = Math.Max(max, 1 + GetMaxDepth(subquery.Statement));
        }

        foreach (var source in SqlAstWalker.OfType<SubquerySource>(statement))
        {
            max = Math.Max(max, 1 + GetMaxDepth(source.Query));
        }

        return max;
    }

    /// <summary>
    /// Enumerates every scalar subquery with its nesting depth (1 = directly
    /// inside the root statement).
    /// </summary>
    /// <param name="root">The root statement.</param>
    public static IEnumerable<(SubqueryExpression Subquery, int Depth)> WithDepth(SelectStatement root)
    {
        foreach (var node in SqlAstWalker.DirectNodes(root))
        {
            if (node is SubqueryExpression subquery)
            {
                yield return (subquery, 1);

                foreach (var inner in WithDepth(subquery.Statement).Select(x => (Subquery: x.Subquery, Depth: x.Depth + 1)))
                {
                    yield return inner;
                }
            }

            if (node is SubquerySource source)
            {
                foreach (var inner in WithDepth(source.Query).Select(x => (Subquery: x.Subquery, Depth: x.Depth + 1)))
                {
                    yield return inner;
                }
            }
        }
    }

    /// <summary>
    /// True when a subquery references columns of the outer scope that are not
    /// declared inside the subquery itself (i.e. it is correlated).
    /// </summary>
    /// <param name="outer">The statement containing the subquery.</param>
    /// <param name="subquery">The subquery to inspect.</param>
    public static bool IsCorrelated(SelectStatement outer, SubqueryExpression subquery)
    {
        var outerScope = GetDirectScopeAliases(outer);
        if (outerScope.Count == 0)
        {
            return false;
        }

        var innerScope = GetDeclaredAliases(subquery.Statement);

        var referenced = ColumnReferenceFinder.FindColumns(subquery.Statement)
            .Where(c => c.TableAlias is not null)
            .Select(c => c.TableAlias!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return referenced.Any(alias => outerScope.Contains(alias) && !innerScope.Contains(alias));
    }

    /// <summary>
    /// Enumerates every subquery directly contained in a statement without
    /// crossing subquery boundaries: scalar subqueries and the subqueries of
    /// EXISTS / IN predicates. Subqueries of derived tables belong to their
    /// inner statement and are reported when that statement is visited.
    /// </summary>
    /// <param name="statement">The statement to inspect.</param>
    public static IEnumerable<SubqueryExpression> FindDirectSubqueries(SelectStatement statement)
    {
        foreach (var node in SqlAstWalker.DirectNodes(statement))
        {
            if (node is SubqueryExpression subquery)
            {
                yield return subquery;
            }
        }
    }

    /// <summary>
    /// Enumerates all aliases (table aliases, derived table aliases and CTE
    /// names) declared anywhere inside a statement, at any nesting level.
    /// </summary>
    /// <param name="statement">The statement to inspect.</param>
    public static HashSet<string> GetDeclaredAliases(SelectStatement statement)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in SqlAstWalker.AllNodes(statement))
        {
            switch (node)
            {
                case TableReference table:
                    aliases.Add(table.Qualifier);
                    break;
                case SubquerySource { Alias: not null } source:
                    aliases.Add(source.Alias!);
                    break;
                case CommonTableExpression cte:
                    aliases.Add(cte.Name);
                    break;
            }
        }

        return aliases;
    }

    /// <summary>
    /// Enumerates the aliases visible to the body of a statement: its own
    /// FROM aliases plus its own CTE names (not including subquery scopes).
    /// </summary>
    /// <param name="statement">The statement.</param>
    public static HashSet<string> GetDirectScopeAliases(SelectStatement statement)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (statement.From is not null)
        {
            CollectFromAliases(statement.From.Source, aliases);
        }

        foreach (var cte in statement.Ctes)
        {
            aliases.Add(cte.Name);
        }

        return aliases;
    }

    /// <summary>
    /// Collects the FROM-level aliases of a source without crossing derived
    /// table boundaries.
    /// </summary>
    /// <param name="source">The FROM source.</param>
    /// <param name="aliases">Accumulator.</param>
    private static void CollectFromAliases(FromSource source, HashSet<string> aliases)
    {
        switch (source)
        {
            case TableReference table:
                aliases.Add(table.Qualifier);
                break;
            case SubquerySource { Alias: not null } subquery:
                aliases.Add(subquery.Alias!);
                break;
            case JoinSource join:
                CollectFromAliases(join.Left, aliases);
                CollectFromAliases(join.Right, aliases);
                break;
        }
    }
}

