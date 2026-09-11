namespace SqlOptimizer.Domain.Metadata;

/// <summary>
/// Result of executing a SQL statement at runtime.
/// </summary>
/// <param name="RowsReturned">Number of rows returned.</param>
/// <param name="Duration">Execution duration.</param>
/// <param name="ColumnNames">Result set column names.</param>
public sealed record QueryExecutionResult(
    int RowsReturned,
    TimeSpan Duration,
    IReadOnlyList<string> ColumnNames);
