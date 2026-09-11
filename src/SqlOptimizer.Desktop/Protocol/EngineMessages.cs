using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlOptimizer.Desktop.Protocol;

/// <summary>Stable operation names (mirrors the engine protocol).</summary>
public static class Operations
{
    /// <summary>Configure the engine pipeline (first operation after startup).</summary>
    public const string Configure = "configure";

    /// <summary>Application/database/LLM status.</summary>
    public const string Health = "health";

    /// <summary>SQL analysis.</summary>
    public const string Analyze = "analyze";

    /// <summary>SQL optimization.</summary>
    public const string Optimize = "optimize";

    /// <summary>Candidate validation.</summary>
    public const string Validate = "validate";

    /// <summary>Cancel an in-flight request by id.</summary>
    public const string Cancel = "cancel";

    /// <summary>Graceful engine shutdown.</summary>
    public const string Shutdown = "shutdown";
}

/// <summary>Stable error codes (mirrors the engine error taxonomy).</summary>
public static class EngineErrors
{
    /// <summary>Protocol-level failure.</summary>
    public const string ProtocolError = "PROTOCOL_ERROR";

    /// <summary>Pipeline operation before configure.</summary>
    public const string NotConfigured = "ENGINE_NOT_CONFIGURED";

    /// <summary>Operation timed out.</summary>
    public const string RequestTimeout = "REQUEST_TIMEOUT";

    /// <summary>Operation canceled by the client.</summary>
    public const string Canceled = "CANCELED";

    /// <summary>Engine busy.</summary>
    public const string EngineBusy = "ENGINE_BUSY";

    /// <summary>Invalid SQL input (empty, too long, non-SELECT).</summary>
    public const string SqlInvalidInput = "SQL_INVALID_INPUT";

    /// <summary>SQL parse error.</summary>
    public const string SqlParseError = "SQL_PARSE_ERROR";

    /// <summary>Unsupported dialect.</summary>
    public const string SqlUnsupportedDialect = "SQL_UNSUPPORTED_DIALECT";

    /// <summary>Validation error.</summary>
    public const string SqlValidation = "SQL_VALIDATION_ERROR";

    /// <summary>Database error.</summary>
    public const string SqlDatabase = "SQL_DATABASE_ERROR";

    /// <summary>Safety guard violation.</summary>
    public const string SqlSafety = "SQL_SAFETY_VIOLATION";

    /// <summary>Analysis error.</summary>
    public const string SqlAnalysis = "SQL_ANALYSIS_ERROR";

    /// <summary>LLM not configured.</summary>
    public const string LlmNotConfigured = "LLM_NOT_CONFIGURED";

    /// <summary>LLM response rejected.</summary>
    public const string LlmInvalidResponse = "LLM_INVALID_RESPONSE";

    /// <summary>Other LLM failure.</summary>
    public const string LlmError = "LLM_ERROR";

    /// <summary>Any other engine failure.</summary>
    public const string Unknown = "UNKNOWN_ERROR";

    /// <summary>Raised locally when the engine process dies.</summary>
    public const string EngineLost = "ENGINE_LOST";
}

/// <summary>JSON settings shared by the client protocol messages.</summary>
public static class ClientJson
{
    /// <summary>Serializer options (camelCase, case-insensitive, string enums).</summary>
    public static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Builds the client serializer options.</summary>
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

/// <summary>
/// A line from the engine: either an engine-initiated message (hello) or a
/// response to a request (correlated by id).
/// </summary>
public sealed class EngineResponse
{
    /// <summary>Correlation id (null for engine-initiated messages).</summary>
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    /// <summary>Message type discriminator (for example "hello").</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>True when the operation succeeded.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    /// <summary>Operation result payload (when ok is true).</summary>
    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }

    /// <summary>Error detail (when ok is false).</summary>
    [JsonPropertyName("error")]
    public EngineErrorInfo? Error { get; set; }
}

/// <summary>Error detail of a failed engine response.</summary>
public sealed class EngineErrorInfo
{
    /// <summary>Stable machine readable code.</summary>
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>Safe human readable message (no secrets, no stack traces).</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
