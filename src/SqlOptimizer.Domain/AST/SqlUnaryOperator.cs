namespace SqlOptimizer.Domain.AST;

/// <summary>Unary operators supported by the SQL AST.</summary>
public enum SqlUnaryOperator
{
    /// <summary>NOT</summary>
    Not,

    /// <summary>- (arithmetic negation)</summary>
    Negate
}
