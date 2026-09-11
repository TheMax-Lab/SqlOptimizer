namespace SqlOptimizer.Application.DTOs;

/// <summary>
/// Outcome of a database-backed comparison between the original query and a
/// candidate. Values are only produced by a real database execution; no
/// implementation may fabricate results.
/// </summary>
public sealed record DatabaseComparisonResult
{
    /// <summary>True when both result sets were equal up to the comparison cap.</summary>
    public required bool ResultsEqual { get; init; }

    /// <summary>Rows returned by the original query (capped at the comparison limit).</summary>
    public required int OriginalRows { get; init; }

    /// <summary>Rows returned by the candidate query (capped at the comparison limit).</summary>
    public required int CandidateRows { get; init; }

    /// <summary>True when either result set hit the comparison cap.</summary>
    public bool Truncated { get; init; }

    /// <summary>Original query execution time when measured.</summary>
    public TimeSpan? OriginalDuration { get; init; }

    /// <summary>Candidate query execution time when measured.</summary>
    public TimeSpan? CandidateDuration { get; init; }

    /// <summary>Notes about the comparison (for example column-name mismatches).</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}
