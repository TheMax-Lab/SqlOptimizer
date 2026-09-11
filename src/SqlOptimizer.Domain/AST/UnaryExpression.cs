using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// A unary expression, for example <c>NOT x</c> or <c>-x</c>.
/// </summary>
/// <param name="Operator">The operator.</param>
/// <param name="Operand">The operand expression.</param>
public sealed record UnaryExpression(SqlUnaryOperator Operator, SqlExpression Operand) : SqlExpression
{
    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children
    {
        get
        {
            yield return Operand;
        }
    }
}
