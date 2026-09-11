using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SqlOptimizer.Api.Tests.TestSupport;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Application.Services;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Parsing;
using SqlOptimizer.Domain.Rules;
using Xunit;

namespace SqlOptimizer.Api.Tests;

/// <summary>
/// M7 offline tests for POST /api/v1/analyze: happy path, request mapping to
/// the Application analyzer, input validation (400 with stable error codes)
/// and the transport-level SQL length quota. No database, no Docker.
/// </summary>
public sealed class AnalyzeEndpointTests
{
    [Fact]
    public async Task ValidSql_Returns200WithScoresFindingsAndStatistics()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/analyze", new { sql = "SELECT * FROM dbo.Customers" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        root.GetProperty("sql").GetString().Should().Be("SELECT * FROM dbo.Customers");
        root.GetProperty("dialect").GetString().Should().Be("SqlServer");
        root.GetProperty("complexityScore").ValueKind.Should().Be(JsonValueKind.Number);
        root.GetProperty("performanceScore").ValueKind.Should().Be(JsonValueKind.Number);
        root.GetProperty("findings").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("findings").GetArrayLength().Should().BeGreaterThan(0, "SELECT * must raise the star-expansion finding");
        root.GetProperty("statistics").GetProperty("tableCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task AstIsNotReturnedByDefault_AndReturnedWhenRequested()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        var withoutAst = await client.PostAsJsonAsync("/api/v1/analyze", new { sql = "SELECT Id FROM dbo.Customers" });
        using (var doc = JsonDocument.Parse(await withoutAst.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("ast").ValueKind.Should().Be(JsonValueKind.Null);
        }

        var withAst = await client.PostAsJsonAsync("/api/v1/analyze", new { sql = "SELECT Id FROM dbo.Customers", includeAst = true });
        using var doc2 = JsonDocument.Parse(await withAst.Content.ReadAsStringAsync());
        doc2.RootElement.GetProperty("ast").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task EmptySql_Returns400WithSqlInvalidInput()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/analyze", new { sql = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertProblemCodeAsync(response, "SQL_INVALID_INPUT");
    }

    [Fact]
    public async Task UnparseableSql_Returns400WithSqlParseError()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/analyze", new { sql = "SELEC 1 FRO" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertProblemCodeAsync(response, "SQL_PARSE_ERROR");
    }

    [Fact]
    public async Task UnsupportedDialect_Returns400WithSqlUnsupportedDialect()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/analyze", new { sql = "SELECT 1", dialect = "MySql" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertProblemCodeAsync(response, "SQL_UNSUPPORTED_DIALECT");
    }

    [Fact]
    public async Task SqlExceedingMaxLength_Returns400WithSqlTooLong_AndDoesNotInvokeAnalyzer()
    {
        var captured = new DelegatingSqlAnalyzer?[1];
        await using var factory = WithAnalyzerProbe(captured, new Dictionary<string, string>
        {
            ["Api__MaxSqlLengthChars"] = "64"
        });
        using var client = factory.CreateDefaultClient();

        var longSql = new string('x', 100);
        var response = await client.PostAsJsonAsync("/api/v1/analyze", new { sql = longSql });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertProblemCodeAsync(response, "SQL_TOO_LONG");

        (captured[0]?.CallCount ?? 0).Should().Be(0, "over-limit input must be rejected before the Application layer");
    }

    [Fact]
    public async Task RequestIsMappedToTheApplicationAnalyzer()
    {
        var captured = new DelegatingSqlAnalyzer?[1];
        await using var factory = WithAnalyzerProbe(captured);
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/analyze", new
        {
            sql = "SELECT Id FROM dbo.Customers ORDER BY Id",
            dialect = "SqlServer",
            includeAst = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        captured[0].Should().NotBeNull("the endpoint must delegate to the Application analyzer");
        var probe = captured[0]!;
        probe.CallCount.Should().Be(1, "the endpoint must delegate to the Application analyzer exactly once");
        probe.LastRequest.Should().NotBeNull();
        var request = probe.LastRequest!;
        request.Sql.Should().Be("SELECT Id FROM dbo.Customers ORDER BY Id");
        request.IncludeAst.Should().BeTrue();
    }

    private static async Task AssertProblemCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("title").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("detail").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("requestId").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("code").GetString().Should().Be(expectedCode);
    }

    private static WebApplicationFactory<Program> WithAnalyzerProbe(
        DelegatingSqlAnalyzer?[] captured,
        IReadOnlyDictionary<string, string>? settings = null) =>
        ApiFactory.Create(settings, configure: builder => builder.ConfigureTestServices(services =>
            // The probe is scoped like the real service, but the first instance
            // created (by the request scope) is captured so the test can observe
            // the exact instance the endpoint used: resolving a fresh scope
            // would create a new instance with an empty counter.
            services.AddScoped<ISqlAnalyzer>(sp => captured[0] ??= new DelegatingSqlAnalyzer(
                new SqlAnalyzer(
                    sp.GetRequiredService<ISqlParser>(),
                    sp.GetRequiredService<RuleRegistry>(),
                    sp.GetRequiredService<IScoreEngine>(),
                    sp.GetRequiredService<SqlOptimizerOptions>())))));
}
