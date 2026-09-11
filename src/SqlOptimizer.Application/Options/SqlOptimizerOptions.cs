namespace SqlOptimizer.Application.Options;

/// <summary>
/// Global SqlOptimizer service configuration.
/// </summary>
public sealed record SqlOptimizerOptions
{
    /// <summary>Maximum accepted SQL length in characters.</summary>
    public int MaxSqlLength { get; init; } = 200_000;

    /// <summary>Default maximum number of LLM candidates.</summary>
    public int MaxCandidates { get; init; } = 3;

    /// <summary>Master switch for any runtime SQL execution (disabled by default).</summary>
    public bool EnableRuntimeValidation { get; init; }

    /// <summary>Log SQL text (disabled by default; never log secrets).</summary>
    public bool LogSql { get; init; }

    /// <summary>Rule threshold options.</summary>
    public SqlOptimizerRulesOptions Rules { get; init; } = new();
}
