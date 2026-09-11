namespace SqlOptimizer.Application.Options;

/// <summary>
/// LLM provider configuration. Secrets (API key) must be supplied via
/// environment variables or user secrets, never committed.
/// The endpoint is any OpenAI-compatible chat completions base URL, which
/// makes the client usable with OpenAI, Azure OpenAI, Ollama and similar
/// providers without code changes.
/// </summary>
/// <param name="Provider">Provider name: "OpenAI" (OpenAI-compatible HTTP) or "Mock". Empty disables the LLM.</param>
/// <param name="Model">Model identifier (for example <c>gpt-4o-mini</c>).</param>
/// <param name="ApiKey">API key. Use environment variable <c>LLM__APIKEY</c> or user secrets.</param>
/// <param name="Endpoint">Base URL of the OpenAI-compatible API (for example <c>https://api.openai.com/v1</c>).</param>
/// <param name="Temperature">Sampling temperature.</param>
/// <param name="TimeoutSeconds">Request timeout in seconds.</param>
/// <param name="MaxPromptChars">Maximum combined system+user prompt length accepted before the LLM step is skipped.</param>
/// <param name="MaxCompletionTokens">Default maximum completion tokens requested from the provider.</param>
public sealed record LlmOptions(
    string Provider = "",
    string Model = "",
    string ApiKey = "",
    string Endpoint = "",
    double Temperature = 0.1,
    int TimeoutSeconds = 120,
    int MaxPromptChars = 60_000,
    int MaxCompletionTokens = 2_000);
