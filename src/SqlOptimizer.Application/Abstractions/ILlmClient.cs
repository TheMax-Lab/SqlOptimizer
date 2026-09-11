namespace SqlOptimizer.Application.Abstractions;

/// <summary>
/// Generic request for a chat completion against an LLM.
/// </summary>
/// <param name="SystemPrompt">System prompt.</param>
/// <param name="UserPrompt">User prompt.</param>
/// <param name="Temperature">Optional sampling temperature override.</param>
/// <param name="MaxTokens">Optional maximum completion tokens.</param>
/// <param name="Model">Optional model identifier override.</param>
public sealed record LlmRequest(
    string SystemPrompt,
    string UserPrompt,
    double? Temperature = null,
    int? MaxTokens = null,
    string? Model = null);

/// <summary>
/// Generic LLM completion response.
/// </summary>
/// <param name="Content">Raw completion text (untrusted input).</param>
/// <param name="Model">Model that produced the response when known.</param>
/// <param name="PromptTokens">Prompt tokens when reported by the provider.</param>
/// <param name="CompletionTokens">Completion tokens when reported by the provider.</param>
/// <param name="DurationSeconds">Round-trip duration when measured.</param>
/// <param name="FinishReason">Provider finish status when reported (for example <c>stop</c>, <c>length</c>).</param>
public sealed record LlmResponse(
    string Content,
    string? Model = null,
    int? PromptTokens = null,
    int? CompletionTokens = null,
    double? DurationSeconds = null,
    string? FinishReason = null)
{
    /// <summary>Non-fatal errors reported by the provider, when any.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>
/// Contract for LLM providers. Implementations: OpenAI-compatible HTTP
/// clients (works with OpenAI, Azure OpenAI, Ollama, ...) and a deterministic
/// mock for offline development.
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Requests a completion.
    /// </summary>
    /// <param name="request">The prompt request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SqlOptimizer.Domain.Common.LlmNotConfiguredException">Provider is not configured.</exception>
    /// <exception cref="SqlOptimizer.Domain.Common.LlmException">The provider call failed.</exception>
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
}
