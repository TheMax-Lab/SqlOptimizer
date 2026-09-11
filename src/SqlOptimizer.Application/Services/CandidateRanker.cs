using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Domain.Analysis;

namespace SqlOptimizer.Application.Services;

/// <summary>
/// Deterministic candidate ranking. Ranking is computed only from the
/// validation status, the validation confidence, the deterministic rule
/// evidence addressed by the candidate and its explicit limitations. The LLM
/// self-reported confidence has a small, capped weight and can never move a
/// candidate across a validation-status tier: a semantically unvalidated
/// candidate can therefore never outrank a validated one. No randomness is
/// involved; ties are broken by candidate id, so the order is stable.
/// </summary>
public sealed class CandidateRanker
{
    // Tier weights dominate every other term, so status ordering can never
    // be overridden by confidence or evidence scores.
    private const double TierValidated = 2_000d;
    private const double TierUnproven = 100d;
    private const double ValidationConfidenceWeight = 50d;
    private const double SelfConfidenceWeight = 20d;
    private const double RuleEvidenceCap = 60d;
    private const double NotePenalty = 5d;

    /// <summary>
    /// Ranks candidates best-first and assigns 1-based ranks.
    /// </summary>
    /// <param name="candidates">Candidates already carrying their validation results.</param>
    /// <param name="analysis">The analysis of the original query (rule evidence).</param>
    public IReadOnlyList<OptimizationCandidate> Rank(
        IReadOnlyList<OptimizationCandidate> candidates,
        SqlAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(analysis);

        var evidence = analysis.Findings
            .GroupBy(f => f.RuleId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(FindingScore).Sum(), StringComparer.Ordinal);

        var ordered = candidates
            .Select(c => (Candidate: c, Score: Score(c, evidence)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Candidate.CandidateId, StringComparer.Ordinal)
            .ToList();

        var result = new List<OptimizationCandidate>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            result.Add(ordered[i].Candidate with { Rank = i + 1 });
        }

        return result;
    }

    /// <summary>Computes the deterministic ranking score of one candidate.</summary>
    private static double Score(
        OptimizationCandidate candidate,
        IReadOnlyDictionary<string, double> evidence)
    {
        var tier = candidate.Status switch
        {
            CandidateStatus.Validated => TierValidated,
            CandidateStatus.Rejected => 0d,
            // Generated/ValidationPending are transient: treated as unproven.
            _ => TierUnproven
        };

        var validationConfidence = (candidate.Validation?.ValidationConfidence ?? 0d) * ValidationConfidenceWeight;

        var ruleEvidence = candidate.RuleIds
            .Distinct(StringComparer.Ordinal)
            .Sum(id => evidence.GetValueOrDefault(id, 0d));
        ruleEvidence = Math.Min(ruleEvidence, RuleEvidenceCap);

        var selfConfidence = Math.Clamp(candidate.Confidence, 0d, 1d) * SelfConfidenceWeight;

        var penalties = (candidate.Warnings.Count + candidate.Limitations.Count + candidate.Assumptions.Count) * NotePenalty;

        return tier + validationConfidence + ruleEvidence + selfConfidence - penalties;
    }

    /// <summary>
    /// Deterministic value of one finding: its expected benefit minus its
    /// risk (all dimensions are 0-10 heuristics from the rule engine).
    /// </summary>
    private static double FindingScore(SqlFinding finding) =>
        Math.Max(
            0d,
            finding.Impact.Performance * 2d + finding.Impact.Readability + finding.Impact.Maintainability - finding.Impact.Risk);
}
