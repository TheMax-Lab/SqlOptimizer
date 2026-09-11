namespace SqlOptimizer.Domain.Optimization;

/// <summary>
/// A proposed index. Recommendations are heuristic: they are based on the
/// query structure and optional schema metadata, not on measured workload
/// statistics, and must be reviewed before being created.
/// </summary>
public sealed record IndexRecommendation
{
    /// <summary>Table to index.</summary>
    public required string Table { get; init; }

    /// <summary>Key columns in proposed order (equality columns first).</summary>
    public required IReadOnlyList<string> KeyColumns { get; init; }

    /// <summary>Optional covering columns.</summary>
    public IReadOnlyList<string> IncludedColumns { get; init; } = [];

    /// <summary>Why the index is proposed.</summary>
    public string? Reason { get; init; }

    /// <summary>Confidence that the index helps, 0-1.</summary>
    public double Confidence { get; init; }

    /// <summary>Heuristic benefit estimate, 0-100.</summary>
    public int EstimatedBenefit { get; init; }
}
