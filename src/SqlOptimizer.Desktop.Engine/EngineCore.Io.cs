using System.Reflection;
using System.Text.Json;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Desktop.Engine.Protocol;
using SqlOptimizer.Desktop.Engine.Support;

namespace SqlOptimizer.Desktop.Engine;

/// <summary>
/// I/O and mapping helpers of the engine core (partial): payload reading,
/// exception mapping, logging and protocol line writing.
/// </summary>
public sealed partial class EngineCore
{
    /// <summary>Message used when a pipeline operation arrives before configure.</summary>
    private const string NotConfiguredMessage =
        "The engine has not been configured yet. Send the 'configure' operation first.";

    /// <summary>Deserializes the request payload, or null when missing/invalid.</summary>
    private static T? ReadPayload<T>(EngineRequest request) where T : class
    {
        if (request.Payload is not { } element)
        {
            return null;
        }

        try
        {
            return element.Deserialize<T>(EngineJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Maps an exception to a stable (code, message) pair. Domain exceptions
    /// carry their safe, user-facing messages; anything else is mapped to a
    /// generic message so internal details never cross the protocol boundary.
    /// </summary>
    private static (string Code, string Message) MapException(Exception ex)
    {
        var (code, message) = ex switch
        {
            SqlInvalidInputException e => (EngineErrors.SqlInvalidInput, e.Message),
            SqlParseException e => (EngineErrors.SqlParseError, e.Message),
            SqlUnsupportedDialectException e => (EngineErrors.SqlUnsupportedDialect, e.Message),
            SqlSafetyException e => (EngineErrors.SqlSafety, e.Message),
            SqlValidationException e => (EngineErrors.SqlValidation, e.Message),
            SqlDatabaseException e => (EngineErrors.SqlDatabase, e.Message),
            SqlAnalysisException e => (EngineErrors.SqlAnalysis, e.Message),
            LlmNotConfiguredException e => (EngineErrors.LlmNotConfigured, e.Message),
            LlmInvalidResponseException e => (EngineErrors.LlmInvalidResponse, e.Message),
            LlmException e => (EngineErrors.LlmError, e.Message),
            SqlOptimizerException e => (EngineErrors.Unknown, e.Message),
            _ => (EngineErrors.Unknown, "An unexpected engine error occurred.")
        };

        return (code, SecretRedactor.Redact(message));
    }

    /// <summary>Resolves the per-request timeout (client value, capped, with a default).</summary>
    private static TimeSpan ResolveTimeout(EngineRequest request)
    {
        var maxMs = (int)ProtocolLimits.MaxRequestTimeout.TotalMilliseconds;
        return request.TimeoutMs is { } ms && ms > 0
            ? TimeSpan.FromMilliseconds(Math.Min(ms, maxMs))
            : ProtocolLimits.DefaultRequestTimeout;
    }

    /// <summary>True when a real LLM client will be active (mirrors AddLlm selection).</summary>
    private static bool IsLlmConfigured(LlmOptions options)
    {
        if (string.Equals(options.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(options.Endpoint) && !string.IsNullOrWhiteSpace(options.ApiKey);
        }

        return string.Equals(options.Provider, "Mock", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Short, secret-free summary of the LLM configuration for logs.</summary>
    private static string LlmSummary(LlmOptions options) =>
        string.IsNullOrWhiteSpace(options.Provider)
            ? "disabled"
            : string.Equals(options.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase)
                ? $"OpenAI ({(string.IsNullOrWhiteSpace(options.Model) ? "model not set" : options.Model!)})"
                : options.Provider;

    /// <summary>Writes an information line to stderr (never to the protocol channel).</summary>
    private static void Log(string message)
    {
        Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [INFO] {SecretRedactor.Redact(message)}");
    }

    /// <summary>Writes an exception summary (type + message, no stack) to stderr.</summary>
    private static void LogException(Exception ex)
    {
        Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [ERROR] {ex.GetType().Name}: {SecretRedactor.Redact(ex.Message)}");
    }

    /// <summary>Version of the engine assembly (major.minor.build).</summary>
    private static string EngineVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>Writes an ok response line for the given correlation id.</summary>
    private Task WriteOkAsync(long id, object payload) =>
        WriteLineAsync(JsonSerializer.Serialize(
            new EngineResponse(id, true, JsonSerializer.SerializeToElement(payload, EngineJson.Options), null),
            EngineJson.Options));

    /// <summary>Writes an error response line for the given correlation id.</summary>
    private Task WriteErrorAsync(long id, string code, string message) =>
        WriteLineAsync(JsonSerializer.Serialize(
            new EngineResponse(id, false, null, new EngineError(code, message)),
            EngineJson.Options));

    /// <summary>Writes a protocol-level error (used for lines too large to parse).</summary>
    public Task WriteProtocolErrorAsync(long id, string message) =>
        WriteErrorAsync(id, EngineErrors.ProtocolError, message);

    /// <summary>Writes one protocol line to stdout (serialized access).</summary>
    private Task WriteLineAsync(string line)
    {
        lock (_stdoutLock)
        {
            _stdout.WriteLine(line);
            _stdout.Flush();
        }

        return Task.CompletedTask;
    }
}
