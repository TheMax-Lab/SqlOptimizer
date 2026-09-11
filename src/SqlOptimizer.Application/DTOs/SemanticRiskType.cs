namespace SqlOptimizer.Application.DTOs;

/// <summary>
/// Kind of semantic risk a candidate transformation may introduce. A risk
/// flag means the validator could not prove the candidate behaves the same
/// as the original along this dimension; it is never proof of a bug.
/// </summary>
public enum SemanticRiskType
{
    /// <summary>NULL handling may change (for example NOT IN vs NOT EXISTS, TRY_CONVERT).</summary>
    NullBehavior,

    /// <summary>Row de-duplication may change (for example DISTINCT removal, UNION vs UNION ALL).</summary>
    DuplicateElimination,

    /// <summary>The number of result rows may change (for example added joins, outer-join filtering).</summary>
    DuplicateRows,

    /// <summary>Join cardinality may change (for example subquery-to-JOIN rewrites).</summary>
    JoinCardinality,

    /// <summary>Result ordering may change; ordering is externally observable.</summary>
    Ordering,

    /// <summary>Aggregation semantics may change (GROUP BY, HAVING, aggregate arguments).</summary>
    AggregationSemantics,

    /// <summary>Outer-join semantics may change (for example predicates moved between ON and WHERE).</summary>
    OuterJoinSemantics,

    /// <summary>Correlated subquery behavior may change under a JOIN rewrite.</summary>
    CorrelatedSubquery,

    /// <summary>Data conversion behavior may change (for example CAST vs CONVERT, type changes).</summary>
    ImplicitConversion,

    /// <summary>String comparison may be collation-sensitive.</summary>
    Collation,

    /// <summary>Predicate logic under SQL three-valued logic may change.</summary>
    PredicateLogic,

    /// <summary>The projected result shape (columns, aliases, star expansion) may change.</summary>
    Projection,

    /// <summary>Statement structure changed (referenced tables, CTEs, set operations).</summary>
    StatementStructure,

    /// <summary>Parameter usage changed; the candidate cannot be invoked the same way.</summary>
    ParameterUsage
}
