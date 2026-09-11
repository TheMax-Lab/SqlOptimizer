namespace SqlOptimizer.Domain.Optimization;

/// <summary>
/// Optimization strategy controlling how aggressively candidates may trade
/// readability/risk for performance.
/// </summary>
public enum OptimizationStrategy
{
    /// <summary>Only propose low risk, semantics preserving changes.</summary>
    Conservative,

    /// <summary>Balanced: include medium risk proposals, clearly flagged.</summary>
    Balanced,

    /// <summary>Aggressive: include higher risk proposals for maximum potential gain.</summary>
    Aggressive
}
