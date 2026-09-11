using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SqlOptimizer.Api.Tests.TestSupport;
using SqlOptimizer.Application.Abstractions;
using Xunit;

namespace SqlOptimizer.Api.Tests;

/// <summary>
/// M11 security-boundary tests: the HTTP surface is a security boundary.
/// It must never provide arbitrary SQL execution, must not bypass the M10
/// validation/security pipeline (ISqlValidator and the read-only execution
/// guard), and must not leak internal details in error responses. All tests
/// are offline and deterministic; no database or Docker is involved.
/// </summary>
public sealed class ApiSecurityBoundaryTests
{
    [Fact]
    public async Task NoSqlExecutionEndpointIsMapped()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        // No route may let a client submit SQL for direct execution. The
        // only SQL-facing endpoints are analyze/optimize/validate, and none
        // of them executes the submitted text outside the guarded pipeline.
        var routes = new[]
        {
            "/api/v1/execute-sql",
            "/api/v1/execute",
            "/api/v1/run-sql",
            "/api/v1/query",
            "/api/execute-sql",
            "/api/execute"
        };

        foreach (var route in routes)
        {
            var response = await client.PostAsJsonAsync(route, new { sql = "SELECT 1" });
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"route {route} must not exist");
        }
    }

    [Fact]
    public async Task WriteStatement_OnOptimize_IsRejectedWith400BeforeAnyPipelineStage()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/optimize",
            new { sql = "DELETE FROM dbo.Customers WHERE Id = 1" });

        // The parser only accepts SELECT statements: a write statement is
        // rejected as a 400 (invalid input) before analysis, optimization or
        // execution.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("SQL_INVALID_INPUT");
    }

    [Fact]
    public async Task WriteStatementAsOriginalSql_OnValidate_IsRejectedWith400()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/validate", new
        {
            originalSql = "DELETE FROM dbo.Customers",
            candidateSql = "SELECT Id FROM dbo.Customers",
            compareResults = true
        });

        // An unparseable (non-SELECT) original is a caller error: 400, and
        // nothing is ever executed.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("SQL_INVALID_INPUT");
    }

    [Fact]
    public async Task UnsafeCandidateSql_OnValidate_IsFailed_AndNeverExecuted()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        // A write statement as a *candidate* is a validation verdict (Failed
        // at the syntax stage), never an execution. With no database
        // configured there is nothing that could run it anyway, and the
        // guarded provider only ever accepts read-only queries.
        var response = await client.PostAsJsonAsync("/api/v1/validate", new
        {
            originalSql = "SELECT Id FROM dbo.Customers ORDER BY Id",
            candidateSql = "UPDATE dbo.Customers SET City = N'Mars'",
            compareResults = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a validation verdict is a 200 outcome");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("Failed");
        root.GetProperty("semanticallyEquivalent").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task InternalErrorResponse_DoesNotExposeSecretsOrStackTrace()
    {
        const string FakeSecret = "Password=Hunter2Secret";
        await using var factory = ApiFactory.Create(configure: builder => builder.ConfigureTestServices(services =>
            services.AddScoped<ISqlOptimizer>(_ => new ThrowingSqlOptimizer(
                new InvalidOperationException($"internal failure: Server=InternalHost;{FakeSecret}")))));
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/optimize",
            new { sql = "SELECT Id FROM dbo.Customers" });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetString().Should().Be("INTERNAL_ERROR");
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(500);

        body.Should().NotContain(FakeSecret);
        body.Should().NotContain("InternalHost");
        body.Should().NotContain("InvalidOperationException");
        body.Should().NotContain("at Microsoft");
    }
}
