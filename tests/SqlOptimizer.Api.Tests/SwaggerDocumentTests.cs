using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using SqlOptimizer.Api.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Api.Tests;

/// <summary>
/// M7 offline tests for the OpenAPI document: it must generate (no schema
/// recursion failures), contain all four /api/v1 endpoints, document the
/// documented error responses and expose concrete request examples. The
/// document is served in the Development environment.
/// </summary>
public sealed class SwaggerDocumentTests
{
    [Fact]
    public async Task SwaggerDocument_GeneratesAndContainsAllApiV1Endpoints()
    {
        await using var factory = ApiFactory.Create(configure: builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateDefaultClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var paths = doc.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/v1/analyze", out var analyze).Should().BeTrue();
        paths.TryGetProperty("/api/v1/optimize", out var optimize).Should().BeTrue();
        paths.TryGetProperty("/api/v1/validate", out var validate).Should().BeTrue();
        paths.TryGetProperty("/api/v1/health", out var health).Should().BeTrue();

        analyze.GetProperty("post").GetProperty("summary").GetString().Should().Be("Analyze a SQL query");
        optimize.GetProperty("post").GetProperty("summary").GetString().Should().Be("Optimize a SQL query");
        validate.GetProperty("post").GetProperty("summary").GetString().Should().Be("Validate a candidate query");
        health.GetProperty("get").GetProperty("summary").GetString().Should().NotBeNullOrEmpty();

        // 200 + 400 responses must be documented for the mutating endpoints.
        analyze.GetProperty("post").GetProperty("responses").GetProperty("200").Should().NotBeNull();
        analyze.GetProperty("post").GetProperty("responses").GetProperty("400").Should().NotBeNull();
    }

    [Fact]
    public async Task SwaggerDocument_ContainsRequestExamples()
    {
        await using var factory = ApiFactory.Create(configure: builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateDefaultClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("SELECT c.Id, c.Name, o.Total FROM dbo.Customers", "the analyze schema must carry a concrete example");
        body.Should().Contain("SELECT * FROM dbo.Customers ORDER BY Id", "the validate schema must carry a concrete example");
        body.Should().Contain("\"Inconclusive\"", "the ValidationResult example must show the conservative Inconclusive outcome");
    }
}
