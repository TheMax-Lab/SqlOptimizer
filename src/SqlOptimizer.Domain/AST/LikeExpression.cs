using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// A LIKE / NOT LIKE pattern predicate.
/// </summary>
/// <param name="Expression">The expression being tested.</param>
/// <param name="Pattern">The pattern expression (typically a string literal).</param>
/// <param name="Escape">Optional escape character expression.</param>
/// <param name="Not">True for NOT LIKE.</param>
public sealed record LikeExpression(
    SqlExpression Expression,
    SqlExpression Pattern,
    SqlExpression? Escape = null,
    bool Not = false) : SqlExpression
{
    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children
    {
        get
        {
            yield return Expression;
            yield return Pattern;

            if (Escape is not null)
            {
                yield return Escape;
            }
        }
    }
}
