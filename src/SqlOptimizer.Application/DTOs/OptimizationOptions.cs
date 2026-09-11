namespace SqlOptimizer.Application.DTOs;

/// <summary>
/// Options controlling the optimization pipeline (see the individual properties).
/// </summary>
public sealed record OptimizationOptions
{
    /// <summary>Request LLM generated candidates.</summary>
    public bool UseLlm { get; init; } = true;

    /// <summary>Produce index recommendations.</summary>
    public bool GenerateIndexes { get; init; } = true;

    /// <summary>Request semantic validation of candidates.</summary>
    public bool ValidateSemantics { get; init; }

    /// <summary>Generate and return the LLM prompt.</summary>
    public bool GeneratePrompt { get; init; } = true;

    /// <summary>Maximum number of LLM candidates (1-10).</summary>
    public int MaxCandidates { get; init; } = 3;

    /// <summary>Optimization strategy.</summary>
    public SqlOptimizer.Domain.Optimization.OptimizationStrategy Strategy { get; init; }
        = SqlOptimizer.Domain.Optimization.OptimizationStrategy.Balanced;
}
