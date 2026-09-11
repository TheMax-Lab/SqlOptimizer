using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Optimization;

namespace SqlOptimizer.Desktop.Engine.Protocol;

/// <summary>
/// Payloads of the desktop engine protocol. They are thin wire shapes only:
/// each is mapped onto the existing Application-layer request records before
/// the established pipeline services are invoked. No business logic lives
/// here.
/// </summary>

/// <summary>Payload of the <c>analyze</c> operation (mirrors SqlAnalysisRequest).</summary>
/// <param name="Sql">The SQL query text.</param>
/// <param name="Dialect">The SQL dialect (default: SqlServer).</param>
/// <param name="IncludeAst">Include the parsed AST in the response.</param>
public sealed record AnalyzePayload(string Sql, SqlDialect? Dialect, bool IncludeAst);

/// <summary>Payload of the <c>optimize</c> operation (mirrors SqlOptimizationRequest).</summary>
/// <param name="Sql">The SQL query text.</param>
/// <param name="Dialect">The SQL dialect (default: SqlServer).</param>
/// <param name="Options">Pipeline options (see <see cref="OptimizeOptionsPayload"/>).</param>
public sealed record OptimizePayload(string Sql, SqlDialect? Dialect, OptimizeOptionsPayload? Options);

/// <summary>Wire shape of the optimization options (mirrors OptimizationOptions).</summary>
/// <param name="UseLlm">Request LLM generated candidates.</param>
/// <param name="GenerateIndexes">Produce index recommendations.</param>
/// <param name="ValidateSemantics">Request semantic validation of candidates.</param>
/// <param name="GeneratePrompt">Generate and return the LLM prompt.</param>
/// <param name="MaxCandidates">Maximum number of LLM candidates (1-10).</param>
/// <param name="Strategy">Optimization strategy (default: Balanced).</param>
public sealed record OptimizeOptionsPayload(
    bool UseLlm,
    bool GenerateIndexes,
    bool ValidateSemantics,
    bool GeneratePrompt,
    int MaxCandidates,
    OptimizationStrategy? Strategy);

/// <summary>Payload of the <c>validate</c> operation (mirrors SqlValidationRequest).</summary>
/// <param name="OriginalSql">The original SQL text.</param>
/// <param name="CandidateSql">The candidate SQL text (untrusted).</param>
/// <param name="CompareResults">Compare result sets when runtime execution is allowed.</param>
/// <param name="MaxRowsForComparison">Safety cap on rows compared (default 1000).</param>
/// <param name="Dialect">The SQL dialect (default: SqlServer).</param>
public sealed record ValidatePayload(
    string OriginalSql,
    string CandidateSql,
    bool CompareResults,
    int? MaxRowsForComparison,
    SqlDialect? Dialect);

/// <summary>Payload of the <c>cancel</c> operation.</summary>
/// <param name="RequestId">Correlation id of the in-flight request to cancel.</param>
public sealed record CancelPayload(long RequestId);


/// <summary>Payload of the <c>configure</c> operation.</summary>
/// <param name="Database">Database options (mirrors the "Database" configuration section).</param>
/// <param name="Llm">LLM options (mirrors the "Llm" configuration section).</param>
/// <param name="Pipeline">Pipeline options (mirrors the "SqlOptimizer" configuration section).</param>
public sealed record ConfigurePayload(
    ConfigureDatabaseOptions? Database,
    ConfigureLlmOptions? Llm,
    ConfigurePipelineOptions? Pipeline);

/// <summary>Wire shape of the database options (mirrors DatabaseOptions).</summary>
/// <param name="ConnectionString">SQL Server connection string (secret; never logged).</param>
/// <param name="Enabled">Master switch for the database-backed providers.</param>
/// <param name="CommandTimeoutSeconds">Command timeout applied to validation statements.</param>
/// <param name="ConnectionTimeoutSeconds">Connection open timeout.</param>
/// <param name="MaxRowsForComparison">Hard cap on rows compared per query.</param>
/// <param name="MaxResultCells">Safety cap on result-set cells materialized.</param>
/// <param name="ApplicationName">Application name reported to SQL Server.</param>
/// <param name="MetadataCacheTtlSeconds">TTL of cached live schema metadata.</param>
/// <param name="MetadataCacheMaxEntries">Bounded cache size of live schema metadata.</param>
public sealed record ConfigureDatabaseOptions(
    string ConnectionString,
    bool Enabled,
    int CommandTimeoutSeconds,
    int ConnectionTimeoutSeconds,
    int MaxRowsForComparison,
    int MaxResultCells,
    string ApplicationName,
    int MetadataCacheTtlSeconds,
    int MetadataCacheMaxEntries);

/// <summary>Wire shape of the LLM options (mirrors LlmOptions).</summary>
/// <param name="Provider">Provider name: "OpenAI" (OpenAI-compatible HTTP) or "Mock". Empty disables the LLM.</param>
/// <param name="Model">Model identifier.</param>
/// <param name="ApiKey">API key (secret; never logged).</param>
/// <param name="Endpoint">Base URL of the OpenAI-compatible API.</param>
/// <param name="Temperature">Sampling temperature.</param>
/// <param name="TimeoutSeconds">Request timeout in seconds.</param>
/// <param name="MaxPromptChars">Maximum combined prompt length accepted before the LLM step is skipped.</param>
/// <param name="MaxCompletionTokens">Default maximum completion tokens requested.</param>
public sealed record ConfigureLlmOptions(
    string Provider,
    string Model,
    string ApiKey,
    string Endpoint,
    double Temperature,
    int TimeoutSeconds,
    int MaxPromptChars,
    int MaxCompletionTokens);

/// <summary>Wire shape of the pipeline options (mirrors SqlOptimizerOptions).</summary>
/// <param name="MaxSqlLength">Maximum accepted SQL length in characters.</param>
/// <param name="MaxCandidates">Default maximum number of LLM candidates.</param>
/// <param name="EnableRuntimeValidation">Master switch for any runtime SQL execution.</param>
/// <param name="LogSql">Log SQL text (disabled by default; never log secrets).</param>
/// <param name="Rules">Configurable rule thresholds.</param>
public sealed record ConfigurePipelineOptions(
    int MaxSqlLength,
    int MaxCandidates,
    bool EnableRuntimeValidation,
    bool LogSql,
    ConfigureRulesOptions? Rules);

/// <summary>Wire shape of the rule thresholds (mirrors SqlOptimizerRulesOptions).</summary>
/// <param name="MaxSubqueryDepth">Maximum subquery depth before SQL014 fires.</param>
/// <param name="LargeInListThreshold">Minimum IN list size before SQL015 fires.</param>
/// <param name="ExcessiveFunctionThreshold">Minimum function count before SQL020 fires.</param>
public sealed record ConfigureRulesOptions(
    int MaxSubqueryDepth,
    int LargeInListThreshold,
    int ExcessiveFunctionThreshold);
