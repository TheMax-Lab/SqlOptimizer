using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SqlOptimizer.Infrastructure.IntegrationTests;
using Xunit;

namespace SqlOptimizer.Api.IntegrationTests;

/// <summary>
/// M7 live integration: POST /api/v1/validate over HTTP with the full M6
/// evidence chain (structure + semantic risk + live metadata + runtime result
/// comparison). The domain ValidationResult is returned as-is with 200;
/// the HTTP layer must never change the outcome.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ValidateLiveIntegrationTests
{
    private readonly TestSqlServerFixture _fixture;

    /// <summary>Creates a new test instance bound to the shared disposable SQL Server.</summary>
    /// <param name="fixture">Shared disposable SQL Server.</param>
    public ValidateLiveIntegrationTests(TestSqlServerFixture fixture) =>
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public async Task LiveMetadataAndRuntimeEquality_ProveSemanticEquivalence()
    {
        _fixture.RequireSnapshotIsolation();
        await using var factory = ApiIntegrationFactory.CreateLive(_fixture);
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/validate", new
        {
            originalSql = "SELECT * FROM dbo.Customers ORDER BY Id",
            candidateSql = "SELECT Id, Name, City FROM dbo.Customers ORDER BY Id",
            compareResults = true,
            maxRowsForComparison = 1000
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        root.GetProperty("status").GetString().Should().Be("Passed");
        root.GetProperty("semanticallyEquivalent").GetBoolean().Should().BeTrue();
        root.GetProperty("validationConfidence").GetDouble().Should().BeGreaterThan(0.9);
        var evidence = root.GetProperty("evidence").EnumerateArray().Select(e => e.GetString()!).ToList();
        evidence.Should().Contain(e => e.Contains("Live schema metadata", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RuntimeMismatch_IsFailed()
    {
        _fixture.RequireSnapshotIsolation();
        await using var factory = ApiIntegrationFactory.CreateLive(_fixture);
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/validate", new
        {
            originalSql = "SELECT Id, Name FROM dbo.Customers WHERE Id < 100 ORDER BY Id",
            candidateSql = "SELECT Id, Name FROM dbo.Customers WHERE Id < 2 ORDER BY Id",
            compareResults = true,
            maxRowsForComparison = 1000
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("status").GetString().Should().Be("Failed");
    }

    [Fact]
    public async Task CountStarVsCountNullableColumn_IsFailedWithLiveMetadata()
    {
        _fixture.RequireSnapshotIsolation();
        await using var factory = ApiIntegrationFactory.CreateLive(_fixture);
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/validate", new
        {
            originalSql = "SELECT COUNT(*) AS C FROM dbo.Orders",
            candidateSql = "SELECT COUNT(Total) AS C FROM dbo.Orders",
            compareResults = true,
            maxRowsForComparison = 1000
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("status").GetString().Should().Be("Failed");
    }

    [Fact]
    public async Task WriteStatementCandidate_IsFailedWithoutExecuting()
    {
        await using var factory = ApiIntegrationFactory.CreateLive(_fixture);
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/validate", new
        {
            originalSql = "SELECT Id FROM dbo.Customers ORDER BY Id",
            candidateSql = "DELETE FROM dbo.Customers",
            compareResults = true,
            maxRowsForComparison = 1000
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("status").GetString().Should().Be("Failed");

        var customers = await _fixture.QueryScalarAsync("SELECT COUNT(*) FROM dbo.Customers");
        customers.Should().Be(4L, "the API must never execute non-read-only SQL");
    }
}
