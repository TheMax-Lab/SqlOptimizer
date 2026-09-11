using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using SqlOptimizer.Api.Endpoints;
using SqlOptimizer.Api.Health;
using SqlOptimizer.Api.Middleware;
using SqlOptimizer.Api.Observability;
using SqlOptimizer.Api.Options;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Application.Services;
using SqlOptimizer.Application.Services.Validation;
using SqlOptimizer.Domain.Parsing;
using SqlOptimizer.Infrastructure;
using SqlOptimizer.Infrastructure.Parsing;
using SqlOptimizer.Infrastructure.Validation;
using SqlOptimizer.Rules;

var builder = WebApplication.CreateBuilder(args);

// API operational options (transport limits, Swagger, API key, rate limiting),
// bound from the "Api" configuration section / environment variables using the
// same explicit parsing pattern as the other option records in this program.
var apiSection = builder.Configuration.GetSection("Api");
var apiOptions = new ApiOptions(
    MaxSqlLengthChars: ParseInt(apiSection["MaxSqlLengthChars"], 16_384),
    MaxRequestBodyBytes: ParseInt(apiSection["MaxRequestBodyBytes"], 131_072),
    EnableSwagger: ParseBool(apiSection["EnableSwagger"], true),
    RequireApiKey: ParseBool(apiSection["RequireApiKey"], false),
    ApiKeyHeaderName: apiSection["ApiKeyHeaderName"] ?? "X-Api-Key",
    ApiKey: apiSection["ApiKey"] ?? string.Empty,
    EnableRateLimiting: ParseBool(apiSection["EnableRateLimiting"], true),
    RateLimitPerMinute: ParseInt(apiSection["RateLimitPerMinute"], 60),
    HealthCheckTimeoutSeconds: ParseInt(apiSection["HealthCheckTimeoutSeconds"], 5));
builder.Services.AddSingleton(apiOptions);

// OpenAPI generation with XML comments and stable schema ids. The Swagger UI
// is exposed only in the Development environment (and when EnableSwagger is
// not explicitly turned off); see the pipeline section below.
// AddEndpointsApiExplorer registers the API description provider required by
// the Swashbuckle generator for the minimal-API endpoints.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SqlOptimizer API",
        Version = "v1",
        Description = "Public HTTP API for SQL analysis, optimization and validation. " +
                      "Conservative semantics: Inconclusive is never a success and no database result is ever fabricated."
    });
    options.CustomSchemaIds(type => type.FullName?.Replace('+', '.'));
    options.SchemaFilter<SqlOptimizer.Api.OpenApi.ApiExamplesSchemaFilter>();
    var xmlPath = Path.Combine(AppContext.BaseDirectory, "SqlOptimizer.Api.xml");
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

// JSON contract for the whole HTTP surface: enums are (de)serialized as their
// stable, documented names, never as opaque numbers. This keeps request and
// response payloads human- and machine-readable ("dialect": "SqlServer",
// "status": "Inconclusive") and matches the OpenAPI document.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// Core services: parser, prompt generator, index advisor, LLM response parser.
builder.Services.AddScoped<ISqlParser, SqlServerSqlParser>();
builder.Services.AddScoped<IPromptGenerator, PromptGenerator>();
builder.Services.AddScoped<IIndexAdvisor, IndexAdvisor>();
builder.Services.AddTransient<LlmResponseParser>();

// Deterministic analysis pipeline: 20 rules + rule registry + score engine
// (stateless singletons), global options and the analyzer use case.
builder.Services.AddSqlOptimizerRules();

// Global pipeline options, now bound from the "SqlOptimizer" configuration
// section (M7, backward-compatible: identical defaults; the only change is
// that values such as EnableRuntimeValidation can be supplied via
// configuration/environment). With all defaults the behavior is exactly
// the pre-M7 behavior.
var sqlOptimizerSection = builder.Configuration.GetSection("SqlOptimizer");
var sqlOptimizerOptions = new SqlOptimizerOptions
{
    MaxSqlLength = ParseInt(sqlOptimizerSection["MaxSqlLength"], 200_000),
    MaxCandidates = ParseInt(sqlOptimizerSection["MaxCandidates"], 3),
    EnableRuntimeValidation = ParseBool(sqlOptimizerSection["EnableRuntimeValidation"], false),
    LogSql = ParseBool(sqlOptimizerSection["LogSql"], false)
};
builder.Services.AddSingleton(sqlOptimizerOptions);
builder.Services.AddScoped<ISqlAnalyzer, SqlAnalyzer>();

// Database validation configuration (M6, optional). The application starts
// and runs fully without a database: only when a connection string is
// configured AND the provider is explicitly enabled do the SQL Server
// validation and live-metadata providers replace the safe unavailable
// provider. Connection strings are never logged.
var dbSection = builder.Configuration.GetSection("Database");
var dbOptions = new DatabaseOptions(
    ConnectionString: dbSection["ConnectionString"] ?? string.Empty,
    Enabled: ParseBool(dbSection["Enabled"], false),
    CommandTimeoutSeconds: ParseInt(dbSection["CommandTimeoutSeconds"], 30),
    ConnectionTimeoutSeconds: ParseInt(dbSection["ConnectionTimeoutSeconds"], 15),
    MaxRowsForComparison: ParseInt(dbSection["MaxRowsForComparison"], 1_000),
    MaxResultCells: ParseInt(dbSection["MaxResultCells"], 20_000),
    ApplicationName: dbSection["ApplicationName"] ?? "SqlOptimizer",
    MetadataCacheTtlSeconds: ParseInt(dbSection["MetadataCacheTtlSeconds"], 300),
    MetadataCacheMaxEntries: ParseInt(dbSection["MetadataCacheMaxEntries"], 256));

builder.Services.AddSingleton(dbOptions);

// API observability and health probe (M7): in-process counters and the
// fast SELECT 1 reachability probe used by GET /api/v1/health.
builder.Services.AddSingleton<ApiMetrics>();
builder.Services.AddSingleton(new DatabaseHealthProbe(dbOptions, apiOptions.HealthCheckTimeoutSeconds));

// Semantic-safety validation: candidate SQL -> validation result. The
// validator picks up an optional live-metadata provider when one is
// registered; without a database it stays fully static.
builder.Services.AddScoped<ISqlValidator, SqlValidator>();

// Database providers (M6/M8, optional): the SQL Server providers are
// registered unconditionally and stay inert until a connection string is
// configured AND Database:Enabled is true, so the application starts and
// runs fully without a database. AddInfrastructure registers the full
// ISqlDatabaseProvider (schema, execution plans, guarded read-only
// execution) plus the focused validation/metadata providers. Connection
// strings are never logged.
builder.Services.AddInfrastructure(dbOptions);

// LLM provider configuration (optional). The application starts and works
// fully without any LLM configured: AddLlm always registers exactly one
// ILlmClient, selected by Llm:Provider ("OpenAI" with endpoint+key, "Mock",
// or an inert UnconfiguredLlmClient fallback), so startup never fails for
// missing LLM credentials and the deterministic path is unchanged.
var llmSection = builder.Configuration.GetSection("Llm");
var llmOptions = new LlmOptions(
    Provider: llmSection["Provider"] ?? string.Empty,
    Model: llmSection["Model"] ?? string.Empty,
    ApiKey: llmSection["ApiKey"] ?? string.Empty,
    Endpoint: llmSection["Endpoint"] ?? string.Empty,
    Temperature: ParseDouble(llmSection["Temperature"], 0.1),
    TimeoutSeconds: ParseInt(llmSection["TimeoutSeconds"], 120),
    MaxPromptChars: ParseInt(llmSection["MaxPromptChars"], 60_000),
    MaxCompletionTokens: ParseInt(llmSection["MaxCompletionTokens"], 2_000));
builder.Services.AddSingleton(llmOptions);
builder.Services.AddLlm(llmOptions);

// Candidate generation and optimization orchestration (M5): deterministic
// plan builder, candidate generators (deterministic + optional LLM),
// deterministic ranking and the end-to-end optimizer use case.
builder.Services.AddScoped<IOptimizationPlanBuilder, OptimizationPlanBuilder>();
builder.Services.AddScoped<ISqlOptimizationCandidateGenerator, DeterministicCandidateGenerator>();
builder.Services.AddScoped<ISqlOptimizationCandidateGenerator, LlmCandidateGenerator>();
builder.Services.AddTransient<CandidateRanker>();
builder.Services.AddScoped<ISqlOptimizer, SqlOptimizationService>();

// Server-wide request body cap (M7): Api:MaxRequestBodyBytes. On .NET 8 the
// transport-level body limit is a Kestrel limit; requests above the cap are
// rejected by the server with 413 before reaching any endpoint.
var maxRequestBodyBytes = Math.Max(1, ParseInt(builder.Configuration["Api:MaxRequestBodyBytes"], 131_072));
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = maxRequestBodyBytes;
});

// Optional per-client rate limiting (M7): token bucket partitioned by remote
// IP with the configured per-minute budget. Disabled entirely via
// Api:EnableRateLimiting=false. Rejections are 429 with Retry-After.
var rateLimitEnabled = apiOptions.EnableRateLimiting;
var rateLimitPerMinute = Math.Max(1, apiOptions.RateLimitPerMinute);
if (rateLimitEnabled)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (onRejectedContext, cancellationToken) =>
        {
            var httpContext = onRejectedContext.HttpContext;
            httpContext.Response.Headers.RetryAfter = "1";
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = SqlOptimizer.Api.Support.HttpStatusDescriptions.Status(StatusCodes.Status429TooManyRequests),
                Detail = "Rate limit exceeded. Retry later.",
                Type = "https://httpstatuses.io/429"
            };
            problem.Extensions["code"] = "RATE_LIMITED";
            problem.Extensions["requestId"] = httpContext.TraceIdentifier;
            await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        };
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
            httpContext => RateLimitPartition.GetTokenBucketLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = rateLimitPerMinute,
                    TokensPerPeriod = rateLimitPerMinute,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1)
                }));
    });
}

var app = builder.Build();

// Startup misconfiguration guard: requiring an API key without configuring
// one would make the API unusable; fail fast with a clear message.
if (apiOptions.RequireApiKey && string.IsNullOrWhiteSpace(apiOptions.ApiKey))
{
    throw new InvalidOperationException(
        "Api:RequireApiKey is true but no API key is configured. Supply it via environment (Api__ApiKey) or user secrets.");
}

// Exception → ProblemDetails mapping (outermost, so every later failure is
// covered). Never echoes raw SQL, parameters or connection strings.
app.UseMiddleware<ProblemDetailsExceptionMiddleware>();

// Swagger UI: Development only, and only when Api:EnableSwagger is not
// explicitly disabled (the default keeps the pre-M7 behavior in Development).
if (app.Environment.IsDevelopment() && apiOptions.EnableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (rateLimitEnabled)
{
    app.UseRateLimiter();
}

// Optional API-key authentication (no-op when Api:RequireApiKey is false).
app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

app.MapGet("/", () => Results.Ok(new
{
    service = "SqlOptimizer",
    status = "running",
    endpoints = new[] { "/health", "/api/v1/analyze", "/api/v1/optimize", "/api/v1/validate", "/api/v1/health", "/swagger" }
}));

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Public versioned API (M7): thin handlers over the Application pipeline.
app.MapV1Api();

app.Run();

// Configuration parsing helpers: unparseable values fall back to the safe default.
static double ParseDouble(string? value, double fallback) =>
    double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : fallback;

static int ParseInt(string? value, int fallback) =>
    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : fallback;

static bool ParseBool(string? value, bool fallback) =>
    bool.TryParse(value, out var parsed)
        ? parsed
        : fallback;

/// <summary>
/// Public marker so <c>WebApplicationFactory&lt;Program&gt;</c> in the test
/// assembly can reference the application entry point (top-level statements).
/// </summary>
public partial class Program
{
}