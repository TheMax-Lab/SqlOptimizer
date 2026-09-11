using SqlOptimizer.Application.DTOs;

namespace SqlOptimizer.Application.Services.Validation;

/// <summary>
/// Stage C of validation: semantic-risk analysis over the two statement
/// snapshots. It detects transformation patterns that require stronger
/// validation because they may change NULL behavior, join cardinality or
/// collation-sensitive comparisons. Every flag is a risk, never a proof, and
/// the analyzer explicitly leaves equivalence undetermined.
/// </summary>
public static class SqlSemanticRiskAnalyzer
{
    /// <summary>
    /// Analyzes the original/candidate pair for transformation patterns that
    /// the static comparison cannot decide.
    /// </summary>
    /// <param name="original">Snapshot of the original statement.</param>
    /// <param name="candidate">Snapshot of the candidate statement.</param>
    public static IReadOnlyList<SemanticRisk> Analyze(
        QueryStructureSnapshot original,
        QueryStructureSnapshot candidate)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(candidate);

        var risks = new List<SemanticRisk>();

        if (original.HasNotInSubquery != candidate.HasNotInSubquery
            || original.HasNotExists != candidate.HasNotExists)
        {
            risks.Add(new SemanticRisk(SemanticRiskType.NullBehavior,
                "NOT IN (subquery) and NOT EXISTS differ on NULL values: when the subquery yields a NULL, NOT IN excludes every row while NOT EXISTS does not. Equivalence cannot be established without data or nullability proof.",
                Provable: false));
        }

        if (original.SubqueryCount > candidate.SubqueryCount
            && candidate.JoinCount >= original.JoinCount)
        {
            risks.Add(new SemanticRisk(SemanticRiskType.CorrelatedSubquery,
                "A correlated subquery was removed in favor of a join; a join rewrite is only equivalent when the inner side has at most one matching row per outer row.",
                Provable: false));

            risks.Add(new SemanticRisk(SemanticRiskType.DuplicateRows,
                "If the join's inner side can produce multiple matches per outer row, the result row count changes.",
                Provable: false));
        }

        var predicateChanged = original.Where != candidate.Where || original.Having != candidate.Having;
        var predicateText = $"{original.Where} {candidate.Where} {original.Having} {candidate.Having}";
        if (predicateChanged && predicateText.Contains("LIKE", StringComparison.OrdinalIgnoreCase))
        {
            risks.Add(new SemanticRisk(SemanticRiskType.Collation,
                "A string pattern comparison changed; the effect may depend on the database collation (case and accent sensitivity).",
                Provable: false));
        }

        if (original.HasCorrelatedSubquery && !candidate.HasCorrelatedSubquery
            && original.SubqueryCount == candidate.SubqueryCount)
        {
            risks.Add(new SemanticRisk(SemanticRiskType.CorrelatedSubquery,
                "Correlated subquery structure changed; correlation behavior (per-row evaluation) may not be preserved.",
                Provable: false));
        }

        return risks;
    }
}
