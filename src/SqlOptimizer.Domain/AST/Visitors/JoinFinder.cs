namespace SqlOptimizer.Domain.AST.Visitors;

/// <summary>
/// Helpers to locate joins in the AST.
/// </summary>
public static class JoinFinder
{
    /// <summary>
    /// Enumerates every join in a FROM source tree (flattening nested joins,
    /// including subquery FROM clauses when <paramref name="includeSubqueries"/>
    /// is set).
    /// </summary>
    /// <param name="source">The FROM source to inspect.</param>
    /// <param name="includeSubqueries">Include joins inside derived tables.</param>
    public static IEnumerable<JoinSource> FindJoins(FromSource? source, bool includeSubqueries = true)
    {
        if (source is null)
        {
            yield break;
        }

        switch (source)
        {
            case JoinSource join:
                foreach (var left in FindJoins(join.Left, includeSubqueries))
                {
                    yield return left;
                }

                yield return join;

                foreach (var right in FindJoins(join.Right, includeSubqueries))
                {
                    yield return right;
                }

                break;
            case SubquerySource subquery:
                if (includeSubqueries && subquery.Query.From is not null)
                {
                    foreach (var inner in FindJoins(subquery.Query.From.Source, includeSubqueries))
                    {
                        yield return inner;
                    }
                }

                break;
        }
    }

    /// <summary>
    /// Enumerates every join of a statement, including joins inside derived
    /// tables, CTEs and set-operation branches.
    /// </summary>
    /// <param name="statement">The statement to inspect.</param>
    public static IEnumerable<JoinSource> FindJoins(SelectStatement statement)
    {
        if (statement.From is not null)
        {
            foreach (var join in FindJoins(statement.From.Source, includeSubqueries: true))
            {
                yield return join;
            }
        }

        foreach (var cte in statement.Ctes)
        {
            foreach (var join in FindJoins(cte.Query))
            {
                yield return join;
            }
        }

        if (statement.SetOperation is not null)
        {
            foreach (var join in FindJoins(statement.SetOperation.Right))
            {
                yield return join;
            }
        }
    }

    /// <summary>
    /// Enumerates outer joins (LEFT/RIGHT/FULL) of a statement.
    /// </summary>
    /// <param name="statement">The statement to inspect.</param>
    public static IEnumerable<JoinSource> FindOuterJoins(SelectStatement statement) =>
        FindJoins(statement).Where(j =>
            j.Type is JoinType.Left or JoinType.Right or JoinType.Full);

    /// <summary>
    /// Enumerates CROSS JOINs of a statement.
    /// </summary>
    /// <param name="statement">The statement to inspect.</param>
    public static IEnumerable<JoinSource> FindCrossJoins(SelectStatement statement) =>
        FindJoins(statement).Where(j => j.Type == JoinType.Cross);
}
