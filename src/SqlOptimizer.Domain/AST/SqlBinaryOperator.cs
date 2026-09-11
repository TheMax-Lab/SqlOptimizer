namespace SqlOptimizer.Domain.AST;

/// <summary>Binary operators supported by the SQL AST.</summary>
public enum SqlBinaryOperator
{
    /// <summary>=</summary>
    Equal,

    /// <summary>&lt;&gt; (not equal)</summary>
    NotEqual,

    /// <summary>&gt;</summary>
    GreaterThan,

    /// <summary>&gt;=</summary>
    GreaterThanOrEqual,

    /// <summary>&lt;</summary>
    LessThan,

    /// <summary>&lt;=</summary>
    LessThanOrEqual,

    /// <summary>AND</summary>
    And,

    /// <summary>OR</summary>
    Or,

    /// <summary>+</summary>
    Add,

    /// <summary>-</summary>
    Subtract,

    /// <summary>*</summary>
    Multiply,

    /// <summary>/</summary>
    Divide,

    /// <summary>IS (operand is NULL)</summary>
    Is,

    /// <summary>IS NOT (operand is not NULL)</summary>
    IsNot
}
