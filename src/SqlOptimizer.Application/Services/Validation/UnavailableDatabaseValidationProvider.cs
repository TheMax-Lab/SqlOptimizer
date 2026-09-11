using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Domain.Common;

namespace SqlOptimizer.Application.Services.Validation;

/// <summary>
/// Default, clearly documented <see cref="IDatabaseValidationProvider"/> for
/// deployments without a database connection. It never connects, never
/// executes SQL and never fabricates results: <see cref="IsAvailable"/> is
/// false and <see cref="CompareResultsAsync"/> always returns null ("no
/// evidence"). Replace it in DI with a real provider (for example a SQL
/// Server implementation) when database-backed validation is configured.
/// </summary>
public sealed class UnavailableDatabaseValidationProvider : IDatabaseValidationProvider
{
    /// <inheritdoc />
    public SqlDialect Dialect => SqlDialect.SqlServer;

    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task<DatabaseComparisonResult?> CompareResultsAsync(
        string originalSql,
        string candidateSql,
        int maxRowsForComparison,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<DatabaseComparisonResult?>(null);
}
