using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;

namespace SqlOptimizer.Application.Abstractions;

/// <summary>
/// Provider of a concrete database connection. The MVP ships a SQL Server
/// implementation; other dialects are future work. All execution goes
/// through the SQL safety guard: only read-only SELECT statements may run.
/// </summary>
public interface ISqlDatabaseProvider
{
    /// <summary>The dialect the provider connects to.</summary>
    SqlDialect Dialect { get; }

    /// <summary>
    /// Reads schema metadata (tables, columns, indexes) from the database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DatabaseSchema> GetSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests the estimated execution plan for a query without executing it.
    /// </summary>
    /// <param name="sql">The query to plan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ExecutionPlan> GetExecutionPlanAsync(string sql, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a read-only query.
    /// </summary>
    /// <param name="sql">The query to execute (must be a SELECT).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<QueryExecutionResult> ExecuteAsync(string sql, CancellationToken cancellationToken = default);
}
