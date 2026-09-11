using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Domain.Analysis;

namespace SqlOptimizer.Api.Tests.TestSupport;

/// <summary>
/// Test double that records every <see cref="SqlAnalysisRequest"/> forwarded
/// to the Application analyzer and then delegates to the real pipeline. Used
/// to prove the HTTP layer maps requests correctly and contains no business
/// logic (it must simply forward).
/// </summary>
public sealed class DelegatingSqlAnalyzer : ISqlAnalyzer
{
    private readonly ISqlAnalyzer _inner;
    private int _calls;

    /// <summary>Creates the delegating analyzer.</summary>
    /// <param name="inner">The real analyzer to delegate to.</param>
    public DelegatingSqlAnalyzer(ISqlAnalyzer inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>Number of invocations recorded so far.</summary>
    public int CallCount => Volatile.Read(ref _calls);

    /// <summary>The most recently forwarded request (null before the first call).</summary>
    public SqlAnalysisRequest? LastRequest { get; private set; }

    /// <inheritdoc />
    public Task<SqlAnalysis> AnalyzeAsync(
        SqlAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        Interlocked.Increment(ref _calls);
        return _inner.AnalyzeAsync(request, cancellationToken);
    }
}
