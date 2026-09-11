using SqlOptimizer.Application.Options;
using SqlOptimizer.Desktop.Engine.Protocol;

namespace SqlOptimizer.Desktop.Engine;

/// <summary>
/// The engine pipeline configuration, mapped from the <c>configure</c>
/// protocol payload onto the existing Application-layer option records. The
/// defaults are identical to the HTTP API startup (see
/// <c>SqlOptimizer.Api/Program.cs</c>) so the desktop surface behaves the
/// same with or without explicit configuration. Unreasonable values are
/// clamped to safe ranges; the existing services keep their own
/// authoritative checks.
/// </summary>
public sealed class EngineConfiguration
{
    /// <summary>Creates the configuration from a wire payload (nulls fall back to API defaults).</summary>
    /// <param name="payload">The configure payload (may be null: all defaults).</param>
    public static EngineConfiguration FromPayload(ConfigurePayload? payload)
    {
        return new EngineConfiguration
        {
            Database = BuildDatabase(payload?.Database),
            Llm = BuildLlm(payload?.Llm),
            Pipeline = BuildPipeline(payload?.Pipeline)
        };
    }

    /// <summary>Database options (mirrors the "Database" configuration section).</summary>
    public DatabaseOptions Database { get; init; } = new();

    /// <summary>LLM options (mirrors the "Llm" configuration section).</summary>
    public LlmOptions Llm { get; init; } = new();

    /// <summary>Pipeline options (mirrors the "SqlOptimizer" configuration section).</summary>
    public SqlOptimizerOptions Pipeline { get; init; } = new();

    /// <summary>Secret values to redact from logs and responses.</summary>
    public IEnumerable<string> Secrets
    {
        get
        {
            var values = new[] { Database.ConnectionString, Llm.ApiKey };
            return values.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct();
        }
    }

    /// <summary>Builds <see cref="DatabaseOptions"/> with API-equivalent defaults.</summary>
    private static DatabaseOptions BuildDatabase(ConfigureDatabaseOptions? p) => new(
        ConnectionString: p?.ConnectionString ?? string.Empty,
        Enabled: p?.Enabled ?? false,
        CommandTimeoutSeconds: Clamp(p?.CommandTimeoutSeconds ?? 30, 1, 3600),
        ConnectionTimeoutSeconds: Clamp(p?.ConnectionTimeoutSeconds ?? 15, 1, 300),
        MaxRowsForComparison: Clamp(p?.MaxRowsForComparison ?? 1_000, 1, 100_000),
        MaxResultCells: Clamp(p?.MaxResultCells ?? 20_000, 1, 1_000_000),
        ApplicationName: string.IsNullOrWhiteSpace(p?.ApplicationName) ? "SqlOptimizer.Desktop" : p!.ApplicationName!,
        MetadataCacheTtlSeconds: Clamp(p?.MetadataCacheTtlSeconds ?? 300, 1, 86_400),
        MetadataCacheMaxEntries: Clamp(p?.MetadataCacheMaxEntries ?? 256, 1, 10_000));

    /// <summary>Builds <see cref="LlmOptions"/> with API-equivalent defaults.</summary>
    private static LlmOptions BuildLlm(ConfigureLlmOptions? p) => new(
        Provider: p?.Provider ?? string.Empty,
        Model: p?.Model ?? string.Empty,
        ApiKey: p?.ApiKey ?? string.Empty,
        Endpoint: p?.Endpoint ?? string.Empty,
        Temperature: p?.Temperature ?? 0.1,
        TimeoutSeconds: Clamp(p?.TimeoutSeconds ?? 120, 1, 3600),
        MaxPromptChars: Clamp(p?.MaxPromptChars ?? 60_000, 1_000, 2_000_000),
        MaxCompletionTokens: Clamp(p?.MaxCompletionTokens ?? 2_000, 1, 100_000));

    /// <summary>Builds <see cref="SqlOptimizerOptions"/> with API-equivalent defaults.</summary>
    private static SqlOptimizerOptions BuildPipeline(ConfigurePipelineOptions? p)
    {
        var rules = p?.Rules;
        return new SqlOptimizerOptions
        {
            MaxSqlLength = Clamp(p?.MaxSqlLength ?? 200_000, 1_000, 2_000_000),
            MaxCandidates = Clamp(p?.MaxCandidates ?? 3, 1, 10),
            EnableRuntimeValidation = p?.EnableRuntimeValidation ?? false,
            LogSql = p?.LogSql ?? false,
            Rules = new SqlOptimizerRulesOptions(
                MaxSubqueryDepth: Clamp(rules?.MaxSubqueryDepth ?? 3, 1, 100),
                LargeInListThreshold: Clamp(rules?.LargeInListThreshold ?? 20, 2, 10_000),
                ExcessiveFunctionThreshold: Clamp(rules?.ExcessiveFunctionThreshold ?? 5, 1, 1_000))
        };
    }

    /// <summary>Clamps a value (or a non-positive supplied value) into [min, max].</summary>
    private static int Clamp(int? value, int min, int max)
    {
        var v = value ?? min;
        return Math.Clamp(v, min, max);
    }
}
