using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// A CAST/CONVERT/TRY_CONVERT expression: <c>CAST(OrderDate AS DATE)</c>,
/// <c>CONVERT(DATETIME2, OrderDate)</c> or
/// <c>TRY_CONVERT(DATE, OrderDate)</c>.
/// </summary>
/// <param name="TargetType">Target data type text (for example <c>DATE</c>, <c>NVARCHAR(100)</c>).</param>
/// <param name="TargetTypeFamily">Normalized type family (for example <c>date</c>, <c>nvarchar</c>, <c>int</c>).</param>
/// <param name="Expression">The expression being converted.</param>
/// <param name="Kind">Which conversion syntax was used.</param>
public sealed record CastExpression(
    string TargetType,
    string TargetTypeFamily,
    SqlExpression Expression,
    CastKind Kind = CastKind.Cast) : SqlExpression
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
