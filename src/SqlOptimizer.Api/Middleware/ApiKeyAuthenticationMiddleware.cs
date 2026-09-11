using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using SqlOptimizer.Api.Options;
using SqlOptimizer.Api.Support;

namespace SqlOptimizer.Api.Middleware;

/// <summary>
/// Optional, lightweight API-key authentication for the public API. When
/// <see cref="ApiOptions.RequireApiKey"/> is false (the default) the middleware
/// passes every request through, so local development and integration tests
/// work out of the box. When enabled, every request under <c>/api</c> must
/// carry the configured key in <see cref="ApiOptions.ApiKeyHeaderName"/>; the
/// comparison is constant-time. The expected key is read only from
/// configuration/environment (never from source), the provided key is never
/// logged, and failures return 401 with a <c>WWW-Authenticate: ApiKey</c>
/// header. For production, pair this with HTTPS and prefer a full identity
/// solution (for example OAuth2/JWT or a managed identity gateway).
/// </summary>
public sealed class ApiKeyAuthenticationMiddleware
{
    /// <summary>Path prefix protected when the API key is required.</summary>
    private const string ProtectedPath = "/api";

    private readonly RequestDelegate _next;
    private readonly ApiOptions _options;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">Next middleware in the pipeline.</param>
    /// <param name="options">Bound API options.</param>
    /// <param name="logger">Logger (never logs key material).</param>
    public ApiKeyAuthenticationMiddleware(
        RequestDelegate next,
        ApiOptions options,
        ILogger<ApiKeyAuthenticationMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Validates the API key when required, then executes the next middleware.</summary>
    /// <param name="context">The HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.RequireApiKey ||
            !context.Request.Path.StartsWithSegments(ProtectedPath, out _))
        {
            await _next(context);
            return;
        }

        if (context.Request.Headers.TryGetValue(_options.ApiKeyHeaderName, out var values) &&
            IsApiKeyValid(values.ToString()))
        {
            await _next(context);
            return;
        }

        // Log the rejection without any key material.
        _logger.LogWarning(
            "Request {Method} {Path} rejected: missing or invalid API key in header {HeaderName}.",
            context.Request.Method,
            context.Request.Path.Value,
            _options.ApiKeyHeaderName);

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "ApiKey";
        await WriteProblemAsync(context);
    }

    private bool IsApiKeyValid(string? provided)
    {
        if (string.IsNullOrEmpty(provided) || string.IsNullOrEmpty(_options.ApiKey))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(_options.ApiKey);
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    private Task WriteProblemAsync(HttpContext context)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = HttpStatusDescriptions.Status(StatusCodes.Status401Unauthorized),
            Detail = $"Provide a valid API key in the '{_options.ApiKeyHeaderName}' header.",
            Type = "https://httpstatuses.io/401",
            Instance = context.Request.Path.HasValue
                ? $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path.Value}"
                : null
        };
        problem.Extensions["code"] = "API_KEY_REQUIRED";
        problem.Extensions["requestId"] = context.TraceIdentifier;
        return context.Response.WriteAsJsonAsync(problem);
    }
}
