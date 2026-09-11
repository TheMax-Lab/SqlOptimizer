using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Domain.Common;

namespace SqlOptimizer.Infrastructure.Llm;

/// <summary>
/// Default LLM client when no provider is configured: every call fails with
/// <see cref="LlmNotConfiguredException"/> so an unconfigured deployment can
/// never fabricate or silently skip LLM behavior.
/// </summary>
public sealed class UnconfiguredLlmClient : ILlmClient
{
    /// <inheritdoc />
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default) =>
        Task.FromException<LlmResponse>(
            new LlmNotConfiguredException("No LLM provider is configured (set Llm:Provider to 'OpenAI' or 'Mock')."));
}
