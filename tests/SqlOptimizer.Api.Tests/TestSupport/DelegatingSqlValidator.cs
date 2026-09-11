using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;

namespace SqlOptimizer.Api.Tests.TestSupport;

/// <summary>
/// Test double that records every <see cref="SqlValidationRequest"/> forwarded
/// to the Application validator and then delegates to the real pipeline. Used
/// to prove the HTTP layer maps requests correctly and contains no business
/// logic (it must simply forward).
/// </summary>
public sealed class DelegatingSqlValidator : ISqlValidator
{
    private readonly ISqlValidator _inner;
    private int _calls;

    /// <summary>Creates the delegating validator.</summary>
    /// <param name="inner">The real validator to delegate to.</param>
    public DelegatingSqlValidator(ISqlValidator inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>Number of invocations recorded so far.</summary>
    public int CallCount => Volatile.Read(ref _calls);

    /// <summary>The most recently forwarded request (null before the first call).</summary>
    public SqlValidationRequest? LastRequest { get; private set; }

    /// <inheritdoc />
    public Task<ValidationResult> ValidateAsync(
        SqlValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        Interlocked.Increment(ref _calls);
        return _inner.ValidateAsync(request, cancellationToken);
    }
}
