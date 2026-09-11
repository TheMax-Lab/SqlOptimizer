namespace SqlOptimizer.Domain.AST;

/// <summary>Kind of join between two FROM sources.</summary>
public enum JoinType
{
    /// <summary>INNER JOIN (including unqualified JOIN).</summary>
    Inner,

    /// <summary>LEFT (OUTER) JOIN.</summary>
    Left,

    /// <summary>RIGHT (OUTER) JOIN.</summary>
    Right,

    /// <summary>FULL (OUTER) JOIN.</summary>
    Full,

    /// <summary>CROSS JOIN.</summary>
    Cross
}
