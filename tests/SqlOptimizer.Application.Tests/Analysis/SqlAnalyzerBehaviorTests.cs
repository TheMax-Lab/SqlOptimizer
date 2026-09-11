using FluentAssertions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Domain.Metadata;
using Xunit;

namespace SqlOptimizer.Application.Tests.Analysis;

/// <summary>
/// Pipeline behavior tests: determinism, metadata effect on confidence,
/// cancellation and score bounds.
/// </summary>
public class SqlAnalyzerBehaviorTests
{
    private static Task<SqlOptimizer.Domain.Analysis.SqlAnalysis> AnalyzeAsync(
        string sql,
        DatabaseSchema? schema = null) =>
        SqlAnalyzerTests.CreateAnalyzer().AnalyzeAsync(new SqlAnalysisRequest
        {
            Sql = sql,
            Schema = schema
        });

    [Fact]
    public async Task Analysis_IsDeterministic()
    {
        var sql = "SELECT c.Id FROM Customers c WHERE c.Id NOT IN (SELECT CustomerId FROM Orders o WHERE o.Total > 100)";

        var first = await AnalyzeAsync(sql);
        var second = await AnalyzeAsync(sql);

        first.ComplexityScore.Should().Be(second.ComplexityScore);
        first.PerformanceScore.Should().Be(second.PerformanceScore);
        first.Findings.Select(f => (f.RuleId, f.Message, f.Confidence))
            .Should().BeEquivalentTo(second.Findings.Select(f => (f.RuleId, f.Message, f.Confidence)));
    }

    [Fact]
    public async Task SchemaMetadata_RaisesConfidence()
    {
        var schema = new DatabaseSchema(
        [
            new DatabaseTable("dbo", "Customers", null,
                [
                    new DatabaseColumn("Id", "INT", false, true),
                    new DatabaseColumn("Name", "NVARCHAR(100)", true, false)
                ], [])
        ]);

        var without = await AnalyzeAsync("SELECT Id FROM Customers WHERE LOWER(Name) = 'john'");
        var withMetadata = await AnalyzeAsync(
            "SELECT Id FROM Customers WHERE LOWER(Name) = 'john'", schema);

        var confidenceWithout = without.Findings.Single(f => f.RuleId == "SQL002").Confidence;
        var confidenceWith = withMetadata.Findings.Single(f => f.RuleId == "SQL002").Confidence;

        confidenceWith.Should().BeGreaterThan(confidenceWithout);
    }

    [Fact]
    public async Task Findings_CarryCompleteSemanticDetails()
    {
        var analysis = await AnalyzeAsync(
            "SELECT * FROM A a CROSS JOIN B b WHERE a.Name LIKE '%x' AND a.Id NOT IN (SELECT Id FROM C)");

        analysis.Findings.Should().NotBeEmpty();

        foreach (var finding in analysis.Findings)
        {
            finding.RuleId.Should().NotBeNullOrEmpty();
            finding.Message.Should().NotBeNullOrEmpty();
            finding.Explanation.Should().NotBeNullOrEmpty();
            finding.Recommendations.Should().NotBeEmpty();
            finding.Confidence.Should().BeInRange(0.0, 1.0);
        }
    }

    [Fact]
    public async Task CancelledToken_ThrowsBeforeAnalysis()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => SqlAnalyzerTests.CreateAnalyzer().AnalyzeAsync(
            new SqlAnalysisRequest { Sql = "SELECT 1" }, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Scores_StayWithinBoundsOnComplexQuery()
    {
        var sql = string.Join(
            " ",
            "SELECT *",
            "FROM A a",
            "CROSS JOIN B b",
            "JOIN C c ON 1 = 1",
            "WHERE a.Name LIKE '%x'",
            "AND a.Id NOT IN (SELECT Id FROM D)",
            "AND YEAR(a.Created) = 2025",
            "AND a.Flag = 1 OR b.Flag = 1");

        var analysis = await AnalyzeAsync(sql);

        analysis.ComplexityScore.Should().BeInRange(0, 100);
        analysis.PerformanceScore.Should().BeInRange(0, 100);
        analysis.Findings.Should().Contain(f => f.RuleId == "SQL012");
        analysis.Findings.Should().Contain(f => f.RuleId == "SQL004");
        analysis.Findings.Should().Contain(f => f.RuleId == "SQL008");
        analysis.Findings.Should().Contain(f => f.RuleId == "SQL002");
    }
}
