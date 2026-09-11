using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SqlOptimizer.Infrastructure.IntegrationTests;
using Xunit;

namespace SqlOptimizer.Api.IntegrationTests;

/// <summary>
/// M7 live integration: POST /api/v1/analyze against the real pipeline and a
/// disposable SQL Server. Without Docker the shared fixture fails fast with
/// an explicit "integration environment unavailable" message.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AnalyzeLiveIntegrationTests
{
    private readonly TestSqlServerFixture _fixture;

    /// <summary>Creates a new test instance bound to the shared disposable SQL Server.</summary>
    /// <param name="fixture">Shared disposable SQL Server.</param>
    public AnalyzeLiveIntegrationTests(TestSqlServerFixture fixture) =>
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public async Task Analyze_JoinQueryAgainstLiveSchema_Returns200WithScoresAndStatistics()
    {
        await using var factory = ApiIntegrationFactory.CreateLive(_fixture);
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/analyze", new
        {
            sql = """
                  SELECT c.Id, c.Name, o.Total
                  FROM dbo.Customers AS c
                  JOIN dbo.Orders AS o ON o.CustomerId = c.Id
                  WHERE c.City = N'Rome'
                  ORDER BY c.Id
                  """
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        root.GetProperty("findings").ValueKind.Should().Be(JsonValueKind.Array);
        root.GetProperty("complexityScore").GetInt32().Should().BeInRange(0, 100);
        root.GetProperty("performanceScore").GetInt32().Should().BeInRange(0, 100);
        root.GetProperty("statistics").GetProperty("tableCount").GetInt32().Should().Be(2);
        root.GetProperty("statistics").GetProperty("joinCount").GetInt32().Should().Be(1);
    }
}
