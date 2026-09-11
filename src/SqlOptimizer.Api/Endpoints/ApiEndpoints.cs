using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SqlOptimizer.Api.Health;
using SqlOptimizer.Api.Observability;
using SqlOptimizer.Api.Options;
using SqlOptimizer.Api.Support;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Options;

namespace SqlOptimizer.Api.Endpoints;

/// <summary>
/// The public, versioned HTTP surface of SqlOptimizer (thin minimal-API
/// handlers): each endpoint only enforces transport-level input limits,
/// forwards the <see cref="HttpContext.RequestAborted"/> token, calls the
/// existing Application services and maps the domain DTOs to the HTTP
/// payload. No business logic lives here, and no validation outcome is ever
/// reinterpreted: a ValidationResult (Passed/Failed/Inconclusive) is always
/// returned as a 200 body, and Inconclusive is never a success.
/// </summary>
public sealed class ApiEndpoints
{
    /// <summary>Prevents instantiation; all members are static.</summary>
    private ApiEndpoints()
    {
    }

    /// <summary>Analyze a SQL query</summary>
    /// <remarks>Validates transport limits and calls the application analyzer; the response honors the request's IncludeAst flag.</remarks>
    internal static async Task<IResult> AnalyzeAsync(
        SqlAnalysisRequest request,
        ISqlAnalyzer analyzer,
        ApiOptions apiOptions,
        ApiMetrics metrics,
        ILogger<ApiEndpoints> logger,
        HttpContext context)
    {
        metrics.Request("analyze");
        var stopwatch = Stopwatch.StartNew();
        var options = apiOptions;

        var invalid = ValidateSqlInput(request.Sql, options.MaxSqlLengthChars);
        if (invalid is not null)
        {
            metrics.Rejected();
            logger.LogWarning("Analyze rejected before dispatch: {Reason} (length {Length}).", invalid.Value.Code, request.Sql?.Length ?? 0);
            return RejectionProblem(context, invalid.Value.Code, invalid.Value.Detail);
        }

        var analysis = await analyzer.AnalyzeAsync(request, context.RequestAborted);
        stopwatch.Stop();
        logger.LogInformation(
            "Analyze completed in {DurationMs} ms: {Findings} finding(s), complexity {Complexity}, performance {Performance}.",
            stopwatch.ElapsedMilliseconds, analysis.Findings.Count, analysis.ComplexityScore, analysis.PerformanceScore);

        return Results.Ok(AnalysisResponse.FromAnalysis(analysis, request.IncludeAst));
    }

    /// <summary>Get API health (liveness and database readiness)</summary>
    /// <remarks>Fast, bounded database ping; never returns secrets.</remarks>
    internal static async Task<IResult> HealthAsync(
        DatabaseHealthProbe probe,
        DatabaseOptions databaseOptions,
        ILogger<ApiEndpoints> logger,
        HttpContext context)
    {
        var configured = probe.IsConfigured;
        var reachable = configured && await probe.PingAsync(context.RequestAborted);
        logger.LogInformation(
            "Health probe: databaseConfigured={Configured}, databaseReachable={Reachable}.",
            configured, reachable);

        var status = !configured || reachable ? "Healthy" : "Degraded";
        return Results.Ok(new ApiHealthResponse(
            status,
            DateTimeOffset.UtcNow,
            new ApiHealthDatabase(configured, reachable, configured ? databaseOptions.MetadataCacheTtlSeconds : null)));
    }

    /// <summary>Optimize a SQL query</summary>
    /// <remarks>
    /// Validates transport limits and calls <see cref="ISqlOptimizer"/>;
    /// the Application pipeline performs generation, mandatory validation and ranking.
    /// </remarks>
    internal static async Task<IResult> OptimizeAsync(
        SqlOptimizationRequest request,
        ISqlOptimizer optimizer,
        ApiOptions apiOptions,
        ApiMetrics metrics,
        ILogger<ApiEndpoints> logger,
        HttpContext context)
    {
        metrics.Request("optimize");
        var stopwatch = Stopwatch.StartNew();
        var options = apiOptions;

        var invalid = ValidateSqlInput(request.Sql, options.MaxSqlLengthChars);
        if (invalid is not null)
        {
            metrics.Rejected();
            logger.LogWarning("Optimize rejected before dispatch: {Reason} (length {Length}).", invalid.Value.Code, request.Sql?.Length ?? 0);
            return RejectionProblem(context, invalid.Value.Code, invalid.Value.Detail);
        }

        var result = await optimizer.OptimizeAsync(request, context.RequestAborted);
        stopwatch.Stop();
        logger.LogInformation(
            "Optimize completed in {DurationMs} ms: {CandidateCount} candidate(s); top validation status: {TopStatus}.",
            stopwatch.ElapsedMilliseconds,
            result.Candidates.Count,
            result.Validation?.Status.ToString() ?? "none");

        return Results.Ok(result);
    }

    /// <summary>Validate a candidate query</summary>
    /// <remarks>
    /// Validates transport limits and calls <see cref="ISqlValidator"/>;
    /// the domain ValidationResult is returned as-is (200 for Passed, Failed and Inconclusive alike).
    /// </remarks>
    internal static async Task<IResult> ValidateAsync(
        SqlValidationRequest request,
        ISqlValidator validator,
        ApiOptions apiOptions,
        ApiMetrics metrics,
        ILogger<ApiEndpoints> logger,
        HttpContext context)
    {
        metrics.Request("validate");
        var stopwatch = Stopwatch.StartNew();
        var options = apiOptions;

        if (string.IsNullOrWhiteSpace(request.OriginalSql) || string.IsNullOrWhiteSpace(request.CandidateSql))
        {
            metrics.Rejected();
            logger.LogWarning("Validate rejected before dispatch: SQL_INVALID_INPUT.");
            return RejectionProblem(context, "SQL_INVALID_INPUT", "Both originalSql and candidateSql must be provided.");
        }

        var tooLong = request.OriginalSql.Length > options.MaxSqlLengthChars
            || request.CandidateSql.Length > options.MaxSqlLengthChars;
        if (tooLong)
        {
            metrics.Rejected();
            logger.LogWarning("Validate rejected before dispatch: SQL_TOO_LONG (max {Max} characters).", options.MaxSqlLengthChars);
            return RejectionProblem(
                context,
                "SQL_TOO_LONG",
                $"Each SQL text must not exceed {options.MaxSqlLengthChars} characters.");
        }

        var result = await validator.ValidateAsync(request, context.RequestAborted);
        stopwatch.Stop();
        metrics.ValidationOutcome(result.Status.ToString());
        logger.LogInformation(
            "Validate completed in {DurationMs} ms: status {Status}, semanticallyEquivalent {Equivalent}.",
            stopwatch.ElapsedMilliseconds, result.Status, result.SemanticallyEquivalent);

        return Results.Ok(result);
    }

    /// <summary>
    /// Transport-level SQL input check: empty/whitespace â†’ SQL_INVALID_INPUT;
    /// over the configured limit â†’ SQL_TOO_LONG. Returns null when the input
    /// passes (the Application layer keeps its own authoritative checks).
    /// </summary>
    /// <param name="sql">The SQL text (possibly null).</param>
    /// <param name="maxLength">Maximum accepted length in characters.</param>
    private static (string Code, string Detail)? ValidateSqlInput(string? sql, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return ("SQL_INVALID_INPUT", "The SQL text must not be empty.");
        }

        if (sql.Length > maxLength)
        {
            return ("SQL_TOO_LONG", $"The SQL text is {sql.Length} characters long; the maximum is {maxLength}.");
        }

        return null;
    }

    /// <summary>Builds a 400 ProblemDetails result with a stable error code and the request id.</summary>
    /// <param name="context">The HTTP context (provides the request id).</param>
    /// <param name="code">Stable machine-readable error code.</param>
    /// <param name="detail">Safe, sanitized detail message (never raw SQL).</param>
    private static IResult RejectionProblem(HttpContext context, string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = HttpStatusDescriptions.Status(StatusCodes.Status400BadRequest),
            Detail = detail,
            Type = "https://httpstatuses.io/400",
            Instance = context.Request.Path.HasValue
                ? $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path.Value}"
                : null
        };
        problem.Extensions["code"] = code;
        problem.Extensions["requestId"] = context.TraceIdentifier;
        return Results.Problem(detail: detail, instance: null, statusCode: StatusCodes.Status400BadRequest, type: null, extensions: problem.Extensions);
    }
}
