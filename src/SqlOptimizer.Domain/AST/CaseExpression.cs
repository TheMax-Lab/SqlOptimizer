using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// A CASE expression.
/// </summary>
/// <param name="Operand">Operand for simple CASE (<c>CASE x WHEN ...</c>), null for searched CASE.</param>
/// <param name="Whens">WHEN/THEN clauses in source order.</param>
/// <param name="Else">ELSE expression when present.</param>
public sealed record CaseExpression(
    SqlExpression? Operand,
    IReadOnlyList<CaseWhenClause> Whens,
    SqlExpression? Else = null) : SqlExpression
{
    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children
    {
        get
        {
            if (Operand is not null)
            {
                yield return Operand;
            }

            foreach (var when in Whens)
            {
                yield return when;
            }

            if (Else is not null)
            {
                yield return Else;
            }
        }
    }
}
