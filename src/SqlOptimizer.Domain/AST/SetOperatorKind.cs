namespace SqlOptimizer.Domain.AST;

/// <summary>Kind of set operation between two SELECT statements.</summary>
public enum SetOperatorKind
{
    /// <summary>UNION (duplicates removed).</summary>
    Union,

    /// <summary>UNION ALL (duplicates kept).</summary>
    UnionAll,

    /// <summary>INTERSECT.</summary>
    Intersect,

    /// <summary>EXCEPT.</summary>
    Except
}
