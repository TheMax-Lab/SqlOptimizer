using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using SqlOptimizer.Api.Support;
using SqlOptimizer.Domain.Common;

namespace SqlOptimizer.Api.Middleware;

/// <summary>
/// Translates pipeline exceptions into RFC 7807 <see cref="ProblemDetails"/>
/// responses with stable error codes and the request id. Bodies contain only
/// sanitized messages: never raw SQL, parameters, connection strings or
/// credentials. Mapping: SqlParseException → 400 SQL_PARSE_ERROR;
/// SqlInvalidInputException → 400 SQL_INVALID_INPUT;
/// SqlUnsupportedDialectException → 400 SQL_UNSUPPORTED_DIALECT; oversized
/// body → 413 PAYLOAD_TOO_LARGE; invalid or missing JSON body (bad payload,
/// unknown enum value, missing required member) → 400 INVALID_REQUEST_BODY;
/// other framework request rejections keep the framework-assigned status;
/// SqlDatabaseException → 503 DATABASE_ERROR;
/// SqlSafetyException → 400 SQL_INVALID_INPUT (safety guard rejection);
/// client cancellation → 408 REQUEST_CANCELLED; else 500 INTERNAL_ERROR
/// (details logged server-side only). Domain validation outcomes (Inconclusive,
/// Failed) are normal 200 bodies and are never mapped to 4xx/5xx.
/// </summary>
public sealed class ProblemDetailsExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemDetailsExceptionMiddleware> _logger;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">Next middleware in the pipeline.</param>
    /// <param name="logger">Logger (server-side details only; never echoed to callers).</param>
    public ProblemDetailsExceptionMiddleware(
        RequestDelegate next,
        ILogger<ProblemDetailsExceptionMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Executes the request and converts unhandled exceptions to ProblemDetails.</summary>
    /// <param name="context">The HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away: report a cancelled request; best effort.
            _logger.LogInformation(
                "Request {Method} {Path} was cancelled by the client.",
                context.Request.Method,
                context.Request.Path.Value);
            await WriteProblemAsync(context, StatusCodes.Status408RequestTimeout, "REQUEST_CANCELLED", "The request was cancelled.");
        }
        catch (SqlParseException ex)
        {
            _logger.LogWarning("Request {Method} {Path} rejected: SQL_PARSE_ERROR.", context.Request.Method, context.Request.Path.Value);
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "SQL_PARSE_ERROR", ex.Message);
        }
        catch (SqlInvalidInputException ex)
        {
            _logger.LogWarning("Request {Method} {Path} rejected: SQL_INVALID_INPUT.", context.Request.Method, context.Request.Path.Value);
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "SQL_INVALID_INPUT", ex.Message);
        }
        catch (SqlSafetyException ex)
        {
            _logger.LogWarning("Request {Method} {Path} rejected: SQL_INVALID_INPUT (safety guard).", context.Request.Method, context.Request.Path.Value);
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "SQL_INVALID_INPUT", ex.Message);
        }
        catch (SqlUnsupportedDialectException ex)
        {
            _logger.LogWarning("Request {Method} {Path} rejected: SQL_UNSUPPORTED_DIALECT.", context.Request.Method, context.Request.Path.Value);
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "SQL_UNSUPPORTED_DIALECT", ex.Message);
        }
        catch (BadHttpRequestException ex)
        {
            // Framework-level request rejections: malformed or missing JSON
            // body (400, including invalid enum values and missing required
            // members), unsupported content type (415), oversized body (413).
            // The status is the one assigned by the framework. The exception
            // message is logged server-side only: it can quote fragments of
            // the submitted payload.
            _logger.LogWarning(
                "Request {Method} {Path} rejected: HTTP {StatusCode} ({BadRequestDetail}).",
                context.Request.Method, context.Request.Path.Value, ex.StatusCode, ex.Message);

            var (code, detail) = ex.StatusCode switch
            {
                StatusCodes.Status413PayloadTooLarge =>
                    ("PAYLOAD_TOO_LARGE", "The request body exceeds the allowed size."),
                _ => ("INVALID_REQUEST_BODY", "The request body is missing or is not valid.")
            };
            await WriteProblemAsync(context, ex.StatusCode, code, detail);
        }
        catch (SqlDatabaseException ex)
        {
            // Infrastructure-level database failure surfaced from the pipeline
            // (validators normally convert it into an Inconclusive result).
            _logger.LogWarning(ex, "Request {Method} {Path} failed: DATABASE_ERROR.", context.Request.Method, context.Request.Path.Value);
            await WriteProblemAsync(context, StatusCodes.Status503ServiceUnavailable, "DATABASE_ERROR", "The database operation could not be completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while processing {Method} {Path}.", context.Request.Method, context.Request.Path.Value);
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "An unexpected error occurred.");
        }
    }

    private static Task WriteProblemAsync(HttpContext context, int statusCode, string code, string detail)
    {
        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = HttpStatusDescriptions.Status(statusCode),
            Detail = detail,
            Type = $"https://httpstatuses.io/{statusCode}",
            Instance = context.Request.Path.HasValue
                ? $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path.Value}"
                : null
        };
        problem.Extensions["code"] = code;
        problem.Extensions["requestId"] = context.TraceIdentifier;
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(problem);
    }
}
