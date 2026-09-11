namespace SqlOptimizer.Application.DTOs;

/// <summary>
/// One optimization candidate proposed by an LLM, parsed and defensively
/// validated from raw (untrusted) model output. A proposal is never a proof:
/// its confidence is self-reported and its SQL must pass validation.
/// </summary>
/// <param name="Sql">The proposed candidate SQL (required, non-empty).</param>
/// <param name="Explanation">Why the candidate should be better, when stated.</param>
/// <param name="ExpectedImpact">Expected optimization impact, when stated.</param>
/// <param name="Confidence">Self-reported confidence, 0-1 (never trusted as proof).</param>
/// <param name="Warnings">Warnings reported by the model.</param>
/// <param name="Assumptions">Assumptions made by the model.</param>
public sealed record LlmCandidate(
    string Sql,
    string? Explanation,
    string? ExpectedImpact,
    double Confidence,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Assumptions);
