using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;

namespace SqlOptimizer.Api.Tests.TestSupport;

/// <summary>
/// <see cref="ISqlOptimizer"/> double that always fails with a configured
/// exception. Used to prove the API maps application-level failures to the
/// documented HTTP status codes (500 INTERNAL_ERROR for unexpected failures,
/// 503 DATABASE_ERROR for <c>SqlDatabaseException</c>) without leaking
/// internal details.
/// </summary>
public sealed class ThrowingSqlOptimizer : ISqlOptimizer
{
    private readonly Exception _exception;

    /// <summary>Creates the double.</summary>
    /// <param name="exception">Exception thrown on every invocation.</param>
    public ThrowingSqlOptimizer(Exception exception) =>
        _exception = exception ?? throw new ArgumentNullException(nameof(exception));

    /// <inheritdoc />
    public Task<SqlOptimizationResult> OptimizeAsync(
        SqlOptimizationRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromException<SqlOptimizationResult>(_exception);
}
