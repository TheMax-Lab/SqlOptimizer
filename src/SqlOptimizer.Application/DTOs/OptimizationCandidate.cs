namespace SqlOptimizer.Application.DTOs;

/// <summary>
/// An optimized SQL candidate produced by the pipeline. A candidate is a
/// proposal: it has not been applied and, until validation proves it
/// preserves the result contract (<see cref="CandidateStatus.Validated"/>),
/// it has not been proven equivalent to the original query. LLM confidence is
/// self-reported and never substitutes for validation.
/// </summary>
/// <param name="CandidateId">Stable identifier of this candidate (for example <c>DET-SQL001</c> or <c>LLM-1</c>).</param>
/// <param name="OriginalSql">The original SQL the candidate rewrites.</param>
/// <param name="CandidateSql">The candidate SQL text (untrusted LLM output when <see cref="Source"/> is <see cref="CandidateSource.Llm"/>).</param>
/// <param name="Source">Where the candidate text came from.</param>
/// <param name="RuleIds">Rule identifiers of the findings the candidate addresses.</param>
/// <param name="Explanation">What the candidate changes and why.</param>
/// <param name="ExpectedOptimization">Expected optimization impact, when known.</param>
/// <param name="Confidence">Self-reported confidence of the source, 0-1 (never a proof).</param>
/// <param name="Warnings">Warnings reported by the source.</param>
/// <param name="Assumptions">Assumptions made by the source.</param>
/// <param name="Limitations">Explicit limitations of this candidate.</param>
/// <param name="Status">Lifecycle status; only validation may set <see cref="CandidateStatus.Validated"/>.</param>
/// <param name="Validation">Validation result when validation was run for this candidate.</param>
/// <param name="Rank">1-based rank in the final ordered result (0 before ranking).</param>
public sealed record OptimizationCandidate(
    string CandidateId,
    string OriginalSql,
    string CandidateSql,
    CandidateSource Source,
    IReadOnlyList<string> RuleIds,
    string Explanation,
    string? ExpectedOptimization,
    double Confidence,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Limitations,
    CandidateStatus Status,
    ValidationResult? Validation = null,
    int Rank = 0);

