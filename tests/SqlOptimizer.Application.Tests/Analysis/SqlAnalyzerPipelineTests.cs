using FluentAssertions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;
using Xunit;

namespace SqlOptimizer.Application.Tests.Analysis;

/// <summary>
/// Continuation of the end-to-end pipeline tests: scenarios 9-15.
/// </summary>
public class SqlAnalyzerPipelineTests
{
    private static Task<SqlOptimizer.Domain.Analysis.SqlAnalysis> AnalyzeAsync(
        string sql,
        SqlDialect dialect = SqlDialect.SqlServer,
        DatabaseSchema? schema = null) =>
        SqlAnalyzerTests.CreateAnalyzer().AnalyzeAsync(new SqlAnalysisRequest
        {
            Sql = sql,
            Dialect = dialect,
            Schema = schema
        });

    // 9. Large IN list
    [Fact]
    public async Task LargeInList_ProducesFinding_SmallListDoesNot()
    {
        var large = string.Join(", ", Enumerable.Range(1, 25));
        var small = "1, 2, 3, 4, 5";

        (await AnalyzeAsync($"SELECT Id FROM T WHERE Id IN ({large})")).Findings
            .Should().Contain(f => f.RuleId == "SQL015");

        (await AnalyzeAsync($"SELECT Id FROM T WHERE Id IN ({small})")).Findings
            .Should().NotContain(f => f.RuleId == "SQL015");
    }

    // 10. Nested subqueries
    [Fact]
    public async Task DeeplyNestedSubqueries_ProduceFinding()
    {
        var analysis = await AnalyzeAsync(
            "SELECT (SELECT (SELECT (SELECT (SELECT MAX(a) FROM T4) FROM T3) FROM T2) FROM T1) FROM T0");

        analysis.Findings.Should().Contain(f => f.RuleId == "SQL014");
        analysis.Statistics.MaxSubqueryDepth.Should().Be(4);
    }

    // 11. CAST/CONVERT
    [Fact]
    public async Task CastOnColumn_ProducesFinding()
    {
        var cast = await AnalyzeAsync(
            "SELECT Id FROM Orders WHERE CAST(OrderDate AS DATE) = '2025-01-01'");
        var convert = await AnalyzeAsync(
            "SELECT Id FROM Orders WHERE CONVERT(DATE, OrderDate) = '2025-01-01'");

        cast.Findings.Should().Contain(f => f.RuleId == "SQL002");
        convert.Findings.Should().Contain(f => f.RuleId == "SQL002");
    }

    // 12. Duplicate expressions
    [Fact]
    public async Task DuplicateExpression_ProducesFinding()
    {
        var analysis = await AnalyzeAsync(
            "SELECT ABS(x - 10) AS A, CASE WHEN ABS(x - 10) > 5 THEN 1 ELSE 0 END FROM T");

        analysis.Findings.Should().Contain(f => f.RuleId == "SQL017");
    }

    // 13. Query with no findings
    [Fact]
    public async Task CleanQuery_HasNoFindingsAndZeroPerformanceScore()
    {
        var analysis = await AnalyzeAsync("SELECT Id, Name FROM Customers WHERE Id = 1");

        analysis.Findings.Should().BeEmpty();
        analysis.PerformanceScore.Should().Be(0);
        analysis.ComplexityScore.Should().BeGreaterThan(0);
    }

    // 14. Invalid SQL
    [Fact]
    public async Task InvalidSql_ThrowsParseError()
    {
        await Assert.ThrowsAsync<SqlParseException>(
            () => AnalyzeAsync("SELECT FROM WHERE"));
    }

    // 15. Unsupported dialect
    [Fact]
    public async Task UnsupportedDialect_Throws()
    {
        var exception = await Assert.ThrowsAsync<SqlUnsupportedDialectException>(
            () => AnalyzeAsync("SELECT Id FROM T", dialect: SqlDialect.PostgreSql));

        exception.Dialect.Should().Be(SqlDialect.PostgreSql);
    }

    [Fact]
    public async Task EmptySql_ThrowsInvalidInput()
    {
        await Assert.ThrowsAsync<SqlInvalidInputException>(
            () => AnalyzeAsync("   "));
    }
}
