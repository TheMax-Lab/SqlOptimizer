using SqlOptimizer.Application.DTOs;

namespace SqlOptimizer.Application.Abstractions;

/// <summary>
/// Result of one candidate generator: the candidates it produced plus the
/// limitations the pipeline must surface (for example "LLM not configured").
/// </summary>
/// <param name="Candidates">Generated candidates, each with status <c>Generated</c>.</param>
/// <param name="Limitations">Explicit limitations of this generation run.</param>
public sealed record CandidateGenerationResult(
    IReadOnlyList<OptimizationCandidate> Candidates,
    IReadOnlyList<string> Limitations)
{
    /// <summary>An empty result with no candidates and no limitations.</summary>
    public static CandidateGenerationResult None { get; } = new([], []);
}

/// <summary>
/// Produces optimization candidates from structured deterministic evidence
/// (the optimization context: analysis, findings, scores, plan, indexes,
/// schema). A generator proposes; it never validates and never declares a
/// candidate safe: every candidate it returns must be passed through
/// <c>ISqlValidator</c> before being considered.
/// </summary>
public interface ISqlOptimizationCandidateGenerator
{
    /// <summary>Generates candidates for the analyzed query, or an empty result.</summary>
    /// <param name="context">The structured optimization context (deterministic evidence).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CandidateGenerationResult> GenerateAsync(
        SqlOptimizationContext context,
        CancellationToken cancellationToken = default);
}
