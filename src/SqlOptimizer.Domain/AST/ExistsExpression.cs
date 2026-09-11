using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// An EXISTS / NOT EXISTS predicate wrapping a subquery.
/// </summary>
/// <param name="Subquery">The subquery.</param>
/// <param name="Not">True for NOT EXISTS.</param>
public sealed record ExistsExpression(SubqueryExpression Subquery, bool Not = false) : SqlExpression
{
    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children
    {
        get
        {
            yield return Subquery;
        }
    }
}
