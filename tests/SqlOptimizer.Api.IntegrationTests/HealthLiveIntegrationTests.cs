using System.Net;
using System.Text.Json;
using FluentAssertions;
using SqlOptimizer.Infrastructure.IntegrationTests;
using Xunit;

namespace SqlOptimizer.Api.IntegrationTests;

/// <summary>
/// M7 live integration: GET /api/v1/health against a reachable disposable SQL
/// Server. The probe must report configured + reachable and never expose
/// connection details.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class HealthLiveIntegrationTests
{
    private readonly TestSqlServerFixture _fixture;

    /// <summary>Creates a new test instance bound to the shared disposable SQL Server.</summary>
    /// <param name="fixture">Shared disposable SQL Server.</param>
    public HealthLiveIntegrationTests(TestSqlServerFixture fixture) =>
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public async Task Health_WithLiveDatabase_ReturnsHealthyAndReachable()
    {
        await using var factory = ApiIntegrationFactory.CreateLive(_fixture);
        using var client = factory.CreateDefaultClient();

        var response = await client.GetAsync("/api/v1/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("status").GetString().Should().Be("Healthy");
        root.GetProperty("database").GetProperty("configured").GetBoolean().Should().BeTrue();
        root.GetProperty("database").GetProperty("reachable").GetBoolean().Should().BeTrue();
        root.GetProperty("database").GetProperty("metadataCacheTtlSeconds").GetInt32().Should().Be(300);
        body.Should().NotContain("Password", "the health payload must never contain credentials");
    }
}
