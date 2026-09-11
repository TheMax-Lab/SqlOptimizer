using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// An IN / NOT IN predicate. Either a literal/parameter value list or a
/// subquery is present, never both.
/// </summary>
/// <param name="Expression">The expression being tested.</param>
/// <param name="Values">Value list when the predicate uses a literal/parameter list.</param>
/// <param name="Subquery">Subquery when the predicate uses <c>IN (SELECT ...)</c>.</param>
/// <param name="Not">True for NOT IN.</param>
public sealed record InExpression(
    SqlExpression Expression,
    IReadOnlyList<SqlExpression> Values,
    SubqueryExpression? Subquery = null,
    bool Not = false) : SqlExpression
{
    /// <summary>True when the predicate tests against a value list.</summary>
    public bool HasValueList => Values.Count > 0;

    /// <summary>True when the predicate tests against a subquery.</summary>
    public bool HasSubquery => Subquery is not null;

    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children
    {
        get
        {
            yield return Expression;

            foreach (var value in Values)
            {
                yield return value;
            }

            if (Subquery is not null)
            {
                yield return Subquery;
            }
        }
    }
}
