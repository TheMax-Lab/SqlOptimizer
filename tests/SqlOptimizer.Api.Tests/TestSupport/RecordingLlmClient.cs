using SqlOptimizer.Application.Abstractions;

namespace SqlOptimizer.Api.Tests.TestSupport;

/// <summary>
/// Counting <see cref="ILlmClient"/> fake used to prove that the HTTP surface
/// never reaches the LLM when <c>UseLlm</c> is false. It records every
/// completion request and returns an empty completion: the defensive
/// <c>LlmResponseParser</c> rejects it, so even a mistaken invocation can
/// never produce a candidate. No network call is ever made.
/// </summary>
public sealed class RecordingLlmClient : ILlmClient
{
    private int _calls;

    /// <summary>Number of completion requests recorded so far.</summary>
    public int CallCount => Volatile.Read(ref _calls);

    /// <inheritdoc />
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(new LlmResponse(string.Empty));
    }
}
