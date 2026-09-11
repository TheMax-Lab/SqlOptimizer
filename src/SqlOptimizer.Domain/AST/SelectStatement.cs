using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// A SELECT statement node. This is the root node produced by the parser for
/// any analyzable query.
/// </summary>
/// <param name="SelectItems">The SELECT list.</param>
/// <param name="From">The FROM clause when present.</param>
/// <param name="Where">The WHERE predicate when present.</param>
/// <param name="GroupBy">GROUP BY expressions.</param>
/// <param name="Having">The HAVING predicate when present.</param>
/// <param name="OrderBy">ORDER BY items.</param>
/// <param name="Distinct">True when SELECT DISTINCT is used.</param>
public sealed record SelectStatement(
    IReadOnlyList<SelectItem> SelectItems,
    FromClause? From,
    SqlExpression? Where,
    IReadOnlyList<SqlExpression> GroupBy,
    SqlExpression? Having,
    IReadOnlyList<OrderByItem> OrderBy,
    bool Distinct) : SqlNode
{
    /// <summary>CTEs declared with a WITH clause for this statement.</summary>
    public IReadOnlyList<CommonTableExpression> Ctes { get; init; } = [];

    /// <summary>Set operation chained to this statement, when present.</summary>
    public SetOperation? SetOperation { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children
    {
        get
        {
            foreach (var item in SelectItems)
            {
                yield return item;
            }

            if (From is not null)
            {
                yield return From;
            }

            if (Where is not null)
            {
                yield return Where;
            }

            foreach (var group in GroupBy)
            {
                yield return group;
            }

            if (Having is not null)
            {
                yield return Having;
            }

            foreach (var order in OrderBy)
            {
                yield return order;
            }

            foreach (var cte in Ctes)
            {
                yield return cte;
            }

            if (SetOperation is not null)
            {
                yield return SetOperation;
            }
        }
    }
}
