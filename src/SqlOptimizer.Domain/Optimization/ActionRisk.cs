namespace SqlOptimizer.Domain.Optimization;

/// <summary>Risk level of an optimization action.</summary>
public enum ActionRisk
{
    /// <summary>Low risk: behavior preserved in virtually all scenarios.</summary>
    Low,

    /// <summary>Medium risk: behavior may change depending on data or configuration.</summary>
    Medium,

    /// <summary>High risk: may change result semantics; requires validation.</summary>
    High
}
