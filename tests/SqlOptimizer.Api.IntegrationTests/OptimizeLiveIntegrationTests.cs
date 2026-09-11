using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SqlOptimizer.Infrastructure.IntegrationTests;
using Xunit;

namespace SqlOptimizer.Api.IntegrationTests;

/// <summary>
/// M7 live integration: POST /api/v1/optimize with live validation enabled.
/// The deterministic star-expansion candidate (schema provided in the request)
/// must come back Validated by the full evidence chain; every candidate
/// carries its own validation and the HTTP layer never reinterprets status.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OptimizeLiveIntegrationTests
{
    private readonly TestSqlServerFixture _fixture;

    /// <summary>Creates a new test instance bound to the shared disposable SQL Server.</summary>
    /// <param name="fixture">Shared disposable SQL Server.</param>
    public OptimizeLiveIntegrationTests(TestSqlServerFixture fixture) =>
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public async Task Optimize_StarQueryWithLiveValidation_ReturnsValidatedCandidate()
    {
        _fixture.RequireSnapshotIsolation();
        await using var factory = ApiIntegrationFactory.CreateLive(_fixture);
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/optimize", new
        {
            sql = "SELECT * FROM dbo.Customers ORDER BY Id",
            options = new { useLlm = false, generatePrompt = false, validateSemantics = true },
            schema = new
            {
                tables = new object[]
                {
                    new
                    {
                        schema = "dbo",
                        name = "Customers",
                        estimatedRowCount = 4,
                        columns = new object[]
                        {
                            new { name = "Id", dataType = "INT", nullable = false, primaryKey = true },
                            new { name = "Name", dataType = "NVARCHAR(100)", nullable = false, primaryKey = false },
                            new { name = "City", dataType = "NVARCHAR(50)", nullable = true, primaryKey = false }
                        },
                        indexes = new object[0]
                    }
                }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        var candidates = root.GetProperty("candidates");
        candidates.ValueKind.Should().Be(JsonValueKind.Array);
        candidates.GetArrayLength().Should().BeGreaterThan(0,
            "the deterministic generator emits the star-expansion candidate when schema metadata covers the table");

        // Ranking: first candidate has rank 1; every candidate carries its own validation.
        var elements = candidates.EnumerateArray().ToList();
        elements[0].GetProperty("rank").GetInt32().Should().Be(1);
        foreach (var candidate in elements)
        {
            candidate.GetProperty("validation").ValueKind.Should().Be(JsonValueKind.Object,
                "every candidate must be validated by the pipeline");
        }

        // The star-expansion candidate is provable live: it must be Validated, never merely Generated.
        elements.Select(e => e.GetProperty("status").GetString()!)
            .Should().Contain("Validated",
            "with live metadata and runtime comparison the star-expansion candidate is proven equivalent");
        elements.Should().NotContain(e => e.GetProperty("status").GetString() == "Rejected",
            "the provably equivalent candidate must not be rejected");
    }
}
