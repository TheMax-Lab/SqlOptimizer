using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlOptimizer.Desktop.Engine.Protocol;

/// <summary>
/// Hard limits of the stdin/stdout JSON protocol. They protect the engine
/// process from pathological input; the application-level limits (for example
/// <c>SqlOptimizerOptions.MaxSqlLength</c>) are still enforced by the existing
/// pipeline services.
/// </summary>
public static class ProtocolLimits
{
    /// <summary>Maximum accepted characters of a single protocol line.</summary>
    public const int MaxLineChars = 2_000_000;

    /// <summary>Maximum number of pipeline operations executed concurrently.</summary>
    public const int MaxConcurrentRequests = 4;

    /// <summary>Default per-request timeout when the client does not send one.</summary>
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Hard ceiling for a client-requested timeout.</summary>
    public static readonly TimeSpan MaxRequestTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Grace period to let in-flight operations finish on shutdown.</summary>
    public static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(5);

    /// <summary>Grace period to drain in-flight operations before a re-configure.</summary>
    public static readonly TimeSpan ConfigureDrainGrace = TimeSpan.FromSeconds(10);
}

/// <summary>Stable operation names of the desktop engine protocol.</summary>
public static class OperationNames
{
    /// <summary>One-time (or re) configuration of the engine pipeline.</summary>
    public const string Configure = "configure";

    /// <summary>Connectivity probe.</summary>
    public const string Ping = "ping";

    /// <summary>Application/database/LLM status (local equivalent of GET /api/v1/health).</summary>
    public const string Health = "health";

    /// <summary>SQL analysis (local equivalent of POST /api/v1/analyze).</summary>
    public const string Analyze = "analyze";

    /// <summary>SQL optimization (local equivalent of POST /api/v1/optimize).</summary>
    public const string Optimize = "optimize";

    /// <summary>Candidate validation (local equivalent of POST /api/v1/validate).</summary>
    public const string Validate = "validate";

    /// <summary>Best-effort cancellation of an in-flight request by id.</summary>
    public const string Cancel = "cancel";

    /// <summary>Graceful engine shutdown.</summary>
    public const string Shutdown = "shutdown";
}

/// <summary>
/// Stable machine readable error codes carried in error responses. The SQL/
/// LLM codes mirror the exception taxonomy of the existing domain layer so
/// the desktop surface behaves consistently with the HTTP API error model.
/// </summary>
public static class EngineErrors
{
    /// <summary>Malformed protocol line or unknown operation.</summary>
    public const string ProtocolError = "PROTOCOL_ERROR";

    /// <summary>A pipeline operation arrived before the configure operation.</summary>
    public const string NotConfigured = "ENGINE_NOT_CONFIGURED";

    /// <summary>The request exceeded its (client-requested) timeout.</summary>
    public const string RequestTimeout = "REQUEST_TIMEOUT";

    /// <summary>The request was canceled by the client.</summary>
    public const string Canceled = "CANCELED";

    /// <summary>The engine is busy (all concurrency slots occupied).</summary>
    public const string EngineBusy = "ENGINE_BUSY";

    /// <summary>Empty/too long/non-SELECT input (SqlInvalidInputException).</summary>
    public const string SqlInvalidInput = "SQL_INVALID_INPUT";

    /// <summary>SQL parse failure (SqlParseException).</summary>
    public const string SqlParseError = "SQL_PARSE_ERROR";

    /// <summary>Unsupported dialect (SqlUnsupportedDialectException).</summary>
    public const string SqlUnsupportedDialect = "SQL_UNSUPPORTED_DIALECT";

    /// <summary>Validation failure (SqlValidationException).</summary>
    public const string SqlValidation = "SQL_VALIDATION_ERROR";

    /// <summary>Database failure (SqlDatabaseException).</summary>
    public const string SqlDatabase = "SQL_DATABASE_ERROR";

    /// <summary>Safety guard rejection (SqlSafetyException).</summary>
    public const string SqlSafety = "SQL_SAFETY_VIOLATION";

    /// <summary>Analysis failure (SqlAnalysisException).</summary>
    public const string SqlAnalysis = "SQL_ANALYSIS_ERROR";

    /// <summary>LLM provider not configured (LlmNotConfiguredException).</summary>
    public const string LlmNotConfigured = "LLM_NOT_CONFIGURED";

    /// <summary>LLM response rejected (LlmInvalidResponseException).</summary>
    public const string LlmInvalidResponse = "LLM_INVALID_RESPONSE";

    /// <summary>Other LLM failure (LlmException).</summary>
    public const string LlmError = "LLM_ERROR";

    /// <summary>Any other failure. The message never contains internal details.</summary>
    public const string Unknown = "UNKNOWN_ERROR";
}


/// <summary>
/// JSON (de)serialization settings for the protocol: enums travel as their
/// stable documented names (matching the HTTP API contract), properties are
/// camelCase and case-insensitive on read.
/// </summary>
public static class EngineJson
{
    /// <summary>Shared serializer options for protocol messages and payloads.</summary>
    public static readonly JsonSerializerOptions Options = Create();

    /// <summary>Builds the protocol serializer options.</summary>
    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

/// <summary>A request line sent by the desktop UI (one JSON object per line).</summary>
/// <param name="Id">Correlation id assigned by the client.</param>
/// <param name="Op">Operation name (see <see cref="OperationNames"/>).</param>
/// <param name="TimeoutMs">Optional per-request timeout in milliseconds.</param>
/// <param name="Payload">Operation-specific payload (may be null).</param>
public sealed record EngineRequest(long Id, string Op, int? TimeoutMs, JsonElement? Payload);

/// <summary>Error detail carried in a failed response.</summary>
/// <param name="Code">Stable machine readable code (see <see cref="EngineErrors"/>).</param>
/// <param name="Message">Safe human readable message (never contains secrets or stack traces).</param>
public sealed record EngineError(string Code, string Message);

/// <summary>A response line written by the engine (one JSON object per line).</summary>
/// <param name="Id">Correlation id of the request.</param>
/// <param name="Ok">True when the operation succeeded.</param>
/// <param name="Payload">Operation result (present when ok is true).</param>
/// <param name="Error">Error detail (present when ok is false).</param>
public sealed record EngineResponse(long Id, bool Ok, JsonElement? Payload, EngineError? Error);

/// <summary>Engine identity sent immediately after startup.</summary>
/// <param name="V">Protocol version.</param>
/// <param name="Type">Message type discriminator.</param>
/// <param name="Engine">Engine metadata.</param>
/// <param name="Capabilities">Supported operations and dialect.</param>
public sealed record EngineHello(int V, string Type, EngineInfo Engine, EngineCapabilities Capabilities);

/// <summary>Engine metadata block of the hello message.</summary>
/// <param name="Name">Engine name.</param>
/// <param name="Version">Engine version.</param>
/// <param name="Runtime">Runtime description (for example ".NET 8.0").</param>
public sealed record EngineInfo(string Name, string Version, string Runtime);

/// <summary>Capability block of the hello message.</summary>
/// <param name="Dialect">Supported SQL dialect.</param>
/// <param name="Operations">Supported operation names.</param>
public sealed record EngineCapabilities(string Dialect, IReadOnlyList<string> Operations);

