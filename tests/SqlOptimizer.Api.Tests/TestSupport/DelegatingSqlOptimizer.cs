using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;

namespace SqlOptimizer.Api.Tests.TestSupport;

/// <summary>
/// Test double that records every <see cref="SqlOptimizationRequest"/> forwarded
/// to the Application optimizer and then delegates to the real pipeline. Used
/// to prove the HTTP layer maps requests correctly and contains no business
/// logic (it must simply forward).
/// </summary>
public sealed class DelegatingSqlOptimizer : ISqlOptimizer
{
    private readonly ISqlOptimizer _inner;
    private int _calls;

    /// <summary>Creates the delegating optimizer.</summary>
    /// <param name="inner">The real optimizer to delegate to.</param>
    public DelegatingSqlOptimizer(ISqlOptimizer inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>Number of invocations recorded so far.</summary>
    public int CallCount => Volatile.Read(ref _calls);

    /// <summary>The most recently forwarded request (null before the first call).</summary>
    public SqlOptimizationRequest? LastRequest { get; private set; }

    /// <inheritdoc />
    public Task<SqlOptimizationResult> OptimizeAsync(
        SqlOptimizationRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        Interlocked.Increment(ref _calls);
        return _inner.OptimizeAsync(request, cancellationToken);
    }
}
