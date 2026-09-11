using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Domain.Common;

namespace SqlOptimizer.Application.Abstractions;

/// <summary>
/// Narrow contract for database-backed validation: executing the original and
/// candidate read-only and comparing their results. Kept separate from
/// <see cref="ISqlDatabaseProvider"/> so the validator depends only on the
/// capability it actually uses and never on schema/plan/execution features it
/// does not need. Implementations must only ever execute read-only SELECT
/// statements and must never fabricate results.
/// </summary>
public interface IDatabaseValidationProvider
{
    /// <summary>The dialect the provider validates against.</summary>
    SqlDialect Dialect { get; }

    /// <summary>True when a real database connection is configured and usable.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Executes the original and the candidate read-only and compares their
    /// result sets. Returns null when the comparison could not be performed
    /// (for example no connection, execution not allowed); null is an
    /// explicit "no evidence" answer, never a success.
    /// </summary>
    /// <param name="originalSql">The original query.</param>
    /// <param name="candidateSql">The candidate query (untrusted input).</param>
    /// <param name="maxRowsForComparison">Safety cap on the rows compared per query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DatabaseComparisonResult?> CompareResultsAsync(
        string originalSql,
        string candidateSql,
        int maxRowsForComparison,
        CancellationToken cancellationToken = default);
}
