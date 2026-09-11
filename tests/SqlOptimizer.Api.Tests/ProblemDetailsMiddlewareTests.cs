using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SqlOptimizer.Api.Middleware;
using SqlOptimizer.Domain.Common;
using Xunit;

namespace SqlOptimizer.Api.Tests;

/// <summary>
/// M12 unit tests for the central exception-to-ProblemDetails mapping. The
/// middleware is exercised directly with an in-memory HttpContext so the
/// framework-level branches (BadHttpRequestException 400/413/415, client
/// cancellation, unhandled errors) can be pinned without a full host: no
/// real Kestrel 413 can be raised under the test transport, so the mapping
/// itself is what these tests verify. Responses must never echo the raw
/// exception message (it can quote fragments of the submitted payload).
/// </summary>
public sealed class ProblemDetailsMiddlewareTests
{
    private static async Task<(HttpContext Context, string Body)> RunAsync(Func<Task> inner, bool requestAborted = false)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/optimize";
        context.Response.Body = new MemoryStream();

        if (requestAborted)
        {
            context.RequestAborted = new CancellationToken(true);
        }

        var middleware = new ProblemDetailsExceptionMiddleware(
            _ => inner(),
            NullLogger<ProblemDetailsExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context, body);
    }

    private static JsonElement Parse(string body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public async Task OversizedBody_MapsTo413WithPayloadTooLargeCode()
    {
        var inner = async () =>
        {
            await Task.CompletedTask;
            throw new BadHttpRequestException(
                "Request body length exceeded maxRequestBodySize=100 (actual 999); payload fragment 'SECRET-FRAGMENT'",
                StatusCodes.Status413PayloadTooLarge);
        };

        var (context, body) = await RunAsync(inner);

        context.Response.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        var root = Parse(body);
        root.GetProperty("code").GetString().Should().Be("PAYLOAD_TOO_LARGE");
        root.GetProperty("requestId").GetString().Should().NotBeNullOrEmpty();
        body.Should().NotContain("SECRET-FRAGMENT",
            "the framework exception message can quote the submitted payload and must never be echoed");
        body.Should().NotContain("999");
    }

    [Fact]
    public async Task MalformedJsonBody_MapsTo400WithInvalidRequestBodyCode()
    {
        var inner = async () =>
        {
            await Task.CompletedTask;
            throw new BadHttpRequestException(
                "Malformed JSON payload: unexpected token 'X' at position 3.",
                StatusCodes.Status400BadRequest);
        };

        var (context, body) = await RunAsync(inner);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        Parse(body).GetProperty("code").GetString().Should().Be("INVALID_REQUEST_BODY");
        body.Should().NotContain("unexpected token");
    }

    [Fact]
    public async Task UnsupportedContentType_MapsTo415KeepingFrameworkStatus()
    {
        var inner = async () =>
        {
            await Task.CompletedTask;
            throw new BadHttpRequestException("content type not supported", StatusCodes.Status415UnsupportedMediaType);
        };

        var (context, body) = await RunAsync(inner);

        context.Response.StatusCode.Should().Be(StatusCodes.Status415UnsupportedMediaType);
        Parse(body).GetProperty("code").GetString().Should().Be("INVALID_REQUEST_BODY");
    }

    [Fact]
    public async Task ClientCancellation_MapsTo408RequestCancelled()
    {
        var inner = async () =>
        {
            await Task.CompletedTask;
            throw new OperationCanceledException();
        };

        var (context, body) = await RunAsync(inner, requestAborted: true);

        // The guaranteed contract is the 408 status. The ProblemDetails body is
        // best effort: WriteAsJsonAsync forwards HttpContext.RequestAborted when
        // no other token is supplied and silently swallows the resulting
        // OperationCanceledException, so with an already-aborted request the
        // body is empty on this runtime. If a runtime completes the write, the
        // payload must still carry the stable REQUEST_CANCELLED code.
        context.Response.StatusCode.Should().Be(StatusCodes.Status408RequestTimeout);
        if (body.Length > 0)
        {
            Parse(body).GetProperty("code").GetString().Should().Be("REQUEST_CANCELLED");
        }
    }

    [Fact]
    public async Task NonClientOperationCanceledException_MapsTo500WithoutDetails()
    {
        // An OperationCanceledException that is NOT caused by client abort
        // (for example an internal deadline) must fall through to the generic
        // 500 mapping and must not be reported as a client cancellation.
        var inner = async () =>
        {
            await Task.CompletedTask;
            throw new OperationCanceledException("internal deadline exceeded for step 'X'");
        };

        var (context, body) = await RunAsync(inner, requestAborted: false);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        Parse(body).GetProperty("code").GetString().Should().Be("INTERNAL_ERROR");
        body.Should().NotContain("internal deadline");
    }

    [Fact]
    public async Task SqlSafetyException_MapsTo400SqlInvalidInput()
    {
        var inner = async () =>
        {
            await Task.CompletedTask;
            throw new SqlSafetyException("rejected by safety guard: INSERT statement");
        };

        var (context, body) = await RunAsync(inner);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        Parse(body).GetProperty("code").GetString().Should().Be("SQL_INVALID_INPUT");
    }

    [Fact]
    public async Task UnhandledException_MapsTo500WithoutAnyDetails()
    {
        var inner = async () =>
        {
            await Task.CompletedTask;
            throw new InvalidOperationException("boom: connection string 'Server=secret-host;Password=hunter2'");
        };

        var (context, body) = await RunAsync(inner);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var root = Parse(body);
        root.GetProperty("code").GetString().Should().Be("INTERNAL_ERROR");
        body.Should().NotContain("boom");
        body.Should().NotContain("secret-host");
        body.Should().NotContain("hunter2");
        body.Should().NotContain("at System.");
    }
}
