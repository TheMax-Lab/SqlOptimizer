using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// One ORDER BY item.
/// </summary>
/// <param name="Expression">The ordering expression.</param>
/// <param name="Ascending">True for ASC (or no direction), false for DESC.</param>
public sealed record OrderByItem(SqlExpression Expression, bool Ascending) : SqlNode
{
    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children
    {
        get
        {
            yield return Expression;
        }
    }
}
