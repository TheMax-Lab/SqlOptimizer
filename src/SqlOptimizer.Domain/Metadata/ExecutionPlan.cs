namespace SqlOptimizer.Domain.Metadata;

/// <summary>
/// A single operator in a (SQL Server) execution plan.
/// </summary>
/// <param name="OperatorType">Physical operator name (for example <c>Index Scan</c>, <c>Hash Match</c>).</param>
/// <param name="EstimatedCost">Estimated cost reported by the optimizer.</param>
/// <param name="EstimatedRows">Estimated row count when available.</param>
/// <param name="ActualRows">Actual row count when available (runtime plans).</param>
/// <param name="ObjectName">Object the operator accesses when available (for example <c>dbo.Orders</c>).</param>
public sealed record PlanOperator(
    string OperatorType,
    double EstimatedCost,
    long? EstimatedRows,
    long? ActualRows,
    string? ObjectName);

/// <summary>
/// A parsed SQL Server execution plan. <see cref="RawPlan"/> preserves the
/// original XML; <see cref="Operators"/> exposes the plan operators.
/// </summary>
/// <param name="RawPlan">Original plan XML.</param>
/// <param name="Operators">Plan operators in document order.</param>
public sealed record ExecutionPlan(
    string RawPlan,
    IReadOnlyList<PlanOperator> Operators);

