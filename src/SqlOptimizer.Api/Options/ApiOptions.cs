namespace SqlOptimizer.Api.Options;

/// <summary>
/// HTTP API operational options, bound from the "Api" configuration section
/// (or environment variables such as <c>Api__MaxSqlLengthChars</c>). These
/// are transport-level limits and switches; they never change the semantics
/// of the Application pipeline. The API key itself must only ever be supplied
/// through environment variables or user secrets, never committed to source.
/// </summary>
/// <param name="MaxSqlLengthChars">
/// Maximum accepted length of any single SQL text in a request (default 16384).
/// Longer input is rejected with 400 before reaching the Application layer.
/// </param>
/// <param name="MaxRequestBodyBytes">
/// Maximum accepted HTTP request body size in bytes (default 131072 = 128 KB),
/// enforced server-wide via Kestrel (<c>Limits.MaxRequestBodySize</c>); larger
/// bodies are rejected with 413 before reaching any endpoint.
/// </param>
/// <param name="EnableSwagger">
/// Master switch for Swagger/OpenAPI. The UI is additionally restricted to the
/// Development environment by default, so this flag can only narrow, not widen,
/// exposure (default true).
/// </param>
/// <param name="RequireApiKey">
/// When true, every request under /api must carry the configured API key in
/// <see cref="ApiKeyHeaderName"/> (default false, for local development).
/// </param>
/// <param name="ApiKeyHeaderName">Name of the header carrying the API key (default "X-Api-Key").</param>
/// <param name="ApiKey">
/// Expected API key value (secret). Must be supplied via environment
/// (<c>Api__ApiKey</c>) or user secrets; the source default is empty, and the
/// application refuses to start with <see cref="RequireApiKey"/> true and an
/// empty key. Never logged.
/// </param>
/// <param name="EnableRateLimiting">
/// Enables the per-client-IP token-bucket rate limiter (default true).
/// </param>
/// <param name="RateLimitPerMinute">
/// Token bucket refill budget per client per minute (default 60).
/// </param>
/// <param name="HealthCheckTimeoutSeconds">
/// Hard timeout for the /api/v1/health database reachability ping (default 5);
/// the probe opens one connection and runs "SELECT 1", nothing heavier.
/// </param>
public sealed record ApiOptions(
    int MaxSqlLengthChars = 16_384,
    int MaxRequestBodyBytes = 131_072,
    bool EnableSwagger = true,
    bool RequireApiKey = false,
    string ApiKeyHeaderName = "X-Api-Key",
    string ApiKey = "",
    bool EnableRateLimiting = true,
    int RateLimitPerMinute = 60,
    int HealthCheckTimeoutSeconds = 5);
