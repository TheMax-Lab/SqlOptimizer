namespace SqlOptimizer.Application.DTOs;

/// <summary>
/// Result of validating an optimized SQL candidate against the original query.
/// <see cref="SemanticallyEquivalent"/> is only true when an actual runtime
/// result comparison proved equivalence; passing syntax or structural checks
/// alone never sets it. Uncertainty is explicit: when equivalence cannot be
/// established the status is <c>Inconclusive</c> and the open risks and
/// limitations are listed, never silently ignored. See the individual
/// properties for details.
/// </summary>
public sealed record ValidationResult
{
    /// <summary>Validation status.</summary>
    public ValidationStatus Status { get; init; }

    /// <summary>True when the candidate parses as valid SQL.</summary>
    public bool SyntaxValid { get; init; }

    /// <summary>True only when proven equivalent by runtime comparison.</summary>
    public bool SemanticallyEquivalent { get; init; }

    /// <summary>Original query execution time when measured.</summary>
    public TimeSpan? OriginalExecutionTime { get; init; }

    /// <summary>Candidate execution time when measured.</summary>
    public TimeSpan? OptimizedExecutionTime { get; init; }

    /// <summary>
    /// Positive percentage when the candidate is faster; reported only when
    /// semantic equivalence has been proven by a runtime result comparison,
    /// null otherwise (a performance claim without proven equivalence is
    /// never made).
    /// </summary>
    public double? ImprovementPercentage { get; init; }

    /// <summary>Observed differences between original and candidate (provable and unverified).</summary>
    public IReadOnlyList<string> Differences { get; init; } = [];

    /// <summary>Fatal validation errors (for example candidate parse errors).</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Semantic-risk flags raised for this candidate pair.</summary>
    public IReadOnlyList<SemanticRisk> SemanticRisks { get; init; } = [];

    /// <summary>
    /// 0-1 confidence in the validation conclusion itself (not in the
    /// candidate's quality): how sure the validator is about its own status.
    /// Deterministic values, documented on the validator.
    /// </summary>
    public double? ValidationConfidence { get; init; }

    /// <summary>Facts that were positively verified (evidence the check holds).</summary>
    public IReadOnlyList<string> Evidence { get; init; } = [];

    /// <summary>Checks that could not be performed or decided, with the reason.</summary>
    public IReadOnlyList<string> Limitations { get; init; } = [];

    /// <summary>Database objects (tables) the validation concerned, when known.</summary>
    public IReadOnlyList<string> AffectedObjects { get; init; } = [];
}
