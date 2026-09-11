using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// A binary expression combining two operands with an operator, for example
/// <c>a = 1</c>, <c>x AND y</c>, <c>OrderDate IS NULL</c> (Is/IsNot with a
/// NULL literal).
/// </summary>
/// <param name="Operator">The operator.</param>
/// <param name="Left">Left operand.</param>
/// <param name="Right">Right operand.</param>
public sealed record BinaryExpression(SqlBinaryOperator Operator, SqlExpression Left, SqlExpression Right) : SqlExpression
{
    /// <summary>Comparison operators that can be evaluated by index seeks.</summary>
    public static readonly IReadOnlySet<SqlBinaryOperator> ComparisonOperators =
        new HashSet<SqlBinaryOperator>
        {
            SqlBinaryOperator.Equal,
            SqlBinaryOperator.NotEqual,
            SqlBinaryOperator.GreaterThan,
            SqlBinaryOperator.GreaterThanOrEqual,
            SqlBinaryOperator.LessThan,
            SqlBinaryOperator.LessThanOrEqual
        };

    /// <summary>Arithmetic operators that make a predicate non-sargable when wrapping a column.</summary>
    public static readonly IReadOnlySet<SqlBinaryOperator> ArithmeticOperators =
        new HashSet<SqlBinaryOperator>
        {
            SqlBinaryOperator.Add,
            SqlBinaryOperator.Subtract,
            SqlBinaryOperator.Multiply,
            SqlBinaryOperator.Divide
        };

    /// <summary>True when the operator is a comparison operator.</summary>
    public bool IsComparison => ComparisonOperators.Contains(Operator);

    /// <summary>True when the operator is an arithmetic operator.</summary>
    public bool IsArithmetic => ArithmeticOperators.Contains(Operator);

    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children
    {
        get
        {
            yield return Left;
            yield return Right;
        }
    }
}
