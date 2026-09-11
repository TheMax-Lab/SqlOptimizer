namespace SqlOptimizer.Domain.Analysis;

/// <summary>Severity of a finding.</summary>
public enum Severity
{
    /// <summary>Informational; no significant performance impact expected.</summary>
    Info,

    /// <summary>Potential issue worth reviewing.</summary>
    Warning,

    /// <summary>Likely significant performance issue.</summary>
    High,

    /// <summary>Severe issue or probable correctness problem.</summary>
    Critical
}
