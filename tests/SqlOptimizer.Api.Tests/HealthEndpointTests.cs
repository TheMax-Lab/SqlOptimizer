using System.Net;
using System.Text.Json;
using FluentAssertions;
using SqlOptimizer.Api.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Api.Tests;

/// <summary>
/// M7 offline tests for GET /api/v1/health: liveness plus database
/// readiness state, with no secrets in the payload. The unreachable-database
/// case uses a local closed port so the probe fails fast without Docker.
/// </summary>
public sealed class HealthEndpointTests
{
    private const string UnreachableConnectionString =
        "Server=127.0.0.1,1;User Id=sa;Password=NeverUsed;TrustServerCertificate=True";

    [Fact]
    public async Task WithoutDatabase_Returns200HealthyAndNotConfigured()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        var response = await client.GetAsync("/api/v1/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("status").GetString().Should().Be("Healthy");
        root.GetProperty("database").GetProperty("configured").GetBoolean().Should().BeFalse();
        root.GetProperty("database").GetProperty("reachable").GetBoolean().Should().BeFalse();
        root.GetProperty("database").TryGetProperty("metadataCacheTtlSeconds", out var ttl)
            .Should().BeTrue();
        ttl.ValueKind.Should().Be(JsonValueKind.Null);
        body.Should().NotContain("Server=", "the health payload must never contain connection details");
    }

    [Fact]
    public async Task ConfiguredButUnreachableDatabase_Returns200DegradedAndNoSecrets()
    {
        await using var factory = ApiFactory.Create(new Dictionary<string, string>
        {
            ["Database__ConnectionString"] = UnreachableConnectionString,
            ["Database__Enabled"] = "true",
            ["Api__HealthCheckTimeoutSeconds"] = "1"
        });
        using var client = factory.CreateDefaultClient();

        var response = await client.GetAsync("/api/v1/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "degraded database is a machine-readable signal, not an HTTP failure");
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("status").GetString().Should().Be("Degraded");
        root.GetProperty("database").GetProperty("configured").GetBoolean().Should().BeTrue();
        root.GetProperty("database").GetProperty("reachable").GetBoolean().Should().BeFalse();
        root.GetProperty("database").GetProperty("metadataCacheTtlSeconds").GetInt32().Should().Be(300);
        body.Should().NotContain("User Id");
        body.Should().NotContain("Password");
        body.Should().NotContain("TrustServerCertificate");
    }
}
