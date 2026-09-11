using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Domain.Common;

namespace SqlOptimizer.Infrastructure.Llm;

/// <summary>
/// OpenAI-compatible chat-completions client (works with OpenAI, Azure
/// OpenAI, Ollama and similar endpoints). The endpoint and API key come
/// from <see cref="LlmOptions"/>; the key is sent only in the Authorization
/// header and is never logged, echoed in errors, or included in the request
/// body. The concrete API path (<c>/chat/completions</c>) is an
/// implementation detail of this class, never exposed through
/// <see cref="ILlmClient"/>. Provider failures surface as
/// <see cref="LlmException"/> (or <see cref="LlmNotConfiguredException"/>
/// when the configuration is incomplete or invalid).
/// </summary>
public sealed class OpenAiCompatibleLlmClient : ILlmClient, IDisposable
{
    /// <summary>
    /// Shared process-wide client for production use (avoids the socket
    /// exhaustion caused by short-lived clients). The per-request timeout is
    /// enforced with a linked cancellation token, so the client-level
    /// timeout only acts as a far backstop.
    /// </summary>
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(300) };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string DefaultModel = "gpt-4o-mini";
    private const string ChatCompletionsPath = "chat/completions";
    private const string Redacted = "[REDACTED]";

    private readonly LlmOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    /// <summary>Creates the client.</summary>
    /// <param name="options">LLM provider options.</param>
    /// <param name="httpHandler">
    /// Optional transport override (used by offline tests with an in-memory
    /// handler). When omitted the shared process-wide client is used.
    /// </param>
    public OpenAiCompatibleLlmClient(LlmOptions options, HttpMessageHandler? httpHandler = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (httpHandler is null)
        {
            _httpClient = SharedHttpClient;
            _ownsHttpClient = false;
        }
        else
        {
            // The linked per-request timeout fully governs this client.
            _httpClient = new HttpClient(httpHandler) { Timeout = Timeout.InfiniteTimeSpan };
            _ownsHttpClient = true;
        }
    }

    /// <inheritdoc />
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(_options.Endpoint) || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new LlmNotConfiguredException("The OpenAI-compatible LLM provider requires 'Llm:Endpoint' and 'Llm:ApiKey'.");
        }

        var endpoint = BuildEndpoint(_options.Endpoint);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        {
            throw new LlmNotConfiguredException("The OpenAI-compatible LLM provider requires a valid absolute 'Llm:Endpoint' URL.");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));

        var payload = new
        {
            model = ResolveModel(request),
            temperature = request.Temperature ?? _options.Temperature,
            max_tokens = request.MaxTokens ?? _options.MaxCompletionTokens,
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt }
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseContentRead, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LlmException($"The LLM request timed out after {_options.TimeoutSeconds} seconds.");
        }
        catch (HttpRequestException ex)
        {
            // Sanitized: the underlying message can contain host names and
            // URIs, which must not reach callers or logs.
            throw new LlmException("The LLM request failed: the provider could not be reached.", ex);
        }

        using (response)
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new LlmException($"The LLM request timed out after {_options.TimeoutSeconds} seconds.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new LlmException($"The LLM provider returned HTTP {(int)response.StatusCode}: {SanitizeBody(body)}");
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                var choice = root.GetProperty("choices").EnumerateArray().First();
                var content = choice.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;

                return new LlmResponse(
                    content,
                    ReadString(root, "model"),
                    ReadInt(root, "usage", "prompt_tokens"),
                    ReadInt(root, "usage", "completion_tokens"),
                    stopwatch.Elapsed.TotalSeconds,
                    ReadString(choice, "finish_reason"));
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new LlmException($"The LLM provider returned an unexpected response: {SanitizeBody(body)}", ex);
            }
        }
    }

    /// <summary>Releases the owned HTTP client, when one was created.</summary>
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    /// <summary>Builds the chat-completions endpoint from the configured base URL (trailing slashes ignored).</summary>
    private static string BuildEndpoint(string baseUri) =>
        baseUri.Trim().TrimEnd('/') + "/" + ChatCompletionsPath;

    /// <summary>Resolves the model: request override, then options, then the default.</summary>
    private string ResolveModel(LlmRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            return request.Model!;
        }

        return string.IsNullOrWhiteSpace(_options.Model) ? DefaultModel : _options.Model;
    }

    /// <summary>Redacts the configured API key from provider error bodies and truncates them.</summary>
    private string SanitizeBody(string body)
    {
        var text = string.IsNullOrWhiteSpace(body) ? "(empty body)" : Truncate(body, 500);
        if (!string.IsNullOrWhiteSpace(_options.ApiKey) && text.Contains(_options.ApiKey, StringComparison.Ordinal))
        {
            text = text.Replace(_options.ApiKey, Redacted, StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>Reads an optional string property, or null.</summary>
    private static string? ReadString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    /// <summary>Reads an optional nested int property, or null.</summary>
    private static int? ReadInt(JsonElement root, string container, string name) =>
        root.TryGetProperty(container, out var usage) &&
        usage.TryGetProperty(name, out var element) &&
        element.ValueKind == JsonValueKind.Number &&
        element.TryGetInt32(out var value)
            ? value
            : null;

    /// <summary>Truncates provider error bodies to keep failures compact.</summary>
    private static string Truncate(string value, int max) =>
        string.IsNullOrWhiteSpace(value) ? "(empty body)" : (value.Length <= max ? value : value[..max] + "...");
}
