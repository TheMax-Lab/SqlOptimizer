namespace SqlOptimizer.Domain.Analysis;

/// <summary>
/// A single issue detected by an optimization rule. A finding is a diagnosis:
/// it describes a potential problem and suggestions. It is NOT a guaranteed
/// optimization; see <c>SqlOptimizer.Domain.Optimization</c> for the
/// finding → action → candidate distinction.
/// </summary>
public sealed record SqlFinding
{
    /// <summary>Rule identifier (for example <c>SQL001</c>).</summary>
    public required string RuleId { get; init; }

    /// <summary>Severity of the issue.</summary>
    public required Severity Severity { get; init; }

    /// <summary>Category of the issue.</summary>
    public required FindingCategory Category { get; init; }

    /// <summary>Short human readable description.</summary>
    public required string Message { get; init; }

    /// <summary>SQL fragment the finding refers to, when available.</summary>
    public string? SqlFragment { get; init; }

    /// <summary>Explanation of why this is an issue.</summary>
    public string? Explanation { get; init; }

    /// <summary>Suggested actions (suggestions, not guarantees).</summary>
    public IReadOnlyList<string> Recommendations { get; init; } = [];

    /// <summary>Confidence that the finding applies, 0-1.</summary>
    public double Confidence { get; init; }

    /// <summary>Estimated impact dimensions.</summary>
    public OptimizationImpact Impact { get; init; } = new(0, 0, 0, 0);
}
