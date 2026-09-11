namespace SqlOptimizer.Application.DTOs;

/// <summary>
/// Parsed and validated LLM optimization response (container). The raw LLM
/// output is untrusted: <c>LlmResponseParser</c> parses, validates and
/// sanitizes it before use. Entries that fail validation are discarded and
/// reported in <see cref="Rejections"/>; they are never silently converted
/// into candidates, and a response with no usable candidate parses to null.
/// </summary>
/// <param name="Candidates">Usable candidate proposals.</param>
public sealed record LlmOptimizationResponse(IReadOnlyList<LlmCandidate> Candidates)
{
    /// <summary>Reasons rejected entries were discarded.</summary>
    public IReadOnlyList<string> Rejections { get; init; } = [];
}
