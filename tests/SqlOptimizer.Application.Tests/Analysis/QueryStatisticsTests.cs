using FluentAssertions;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Infrastructure.Parsing;
using Xunit;

namespace SqlOptimizer.Application.Tests.Analysis;

/// <summary>
/// M12 tests for the deterministic structural statistics computed from an
/// AST: counts must reflect the exact structure and repeated computation on
/// the same input must be stable. All inputs are parsed with the production
/// parser; no mocks.
/// </summary>
public class QueryStatisticsTests
{
    private static QueryStatistics StatsFor(string sql) =>
        QueryStatistics.FromAst(new SqlServerSqlParser().Parse(sql).Root);

    [Fact]
    public void SimpleSelect_HasBasicCounts()
    {
        var stats = StatsFor("SELECT Id, Name FROM dbo.Customers WHERE Id > 10");

        stats.TableCount.Should().Be(1);
        stats.JoinCount.Should().Be(0);
        stats.SubqueryCount.Should().Be(0);
        stats.PredicateCount.Should().Be(1);
        stats.SelectColumnCount.Should().Be(2);
        stats.HasDistinct.Should().BeFalse();
        stats.HasUnion.Should().BeFalse();
        stats.OrderByCount.Should().Be(0);
        stats.CteCount.Should().Be(0);
    }

    [Fact]
    public void JoinQuery_CountsJoins()
    {
        var stats = StatsFor(
            "SELECT c.Id FROM dbo.Customers c INNER JOIN dbo.Orders o ON o.CustomerId = c.Id");

        stats.TableCount.Should().Be(2);
        stats.JoinCount.Should().Be(1);
    }

    [Fact]
    public void NestedSubqueries_AreCountedWithDepth()
    {
        var stats = StatsFor(
            """
            SELECT c.Id FROM dbo.Customers c
            WHERE EXISTS (SELECT 1 FROM dbo.Orders o WHERE o.CustomerId = c.Id)
            AND c.Id IN (SELECT CustomerId FROM dbo.VIPs v WHERE v.Lvl IN (SELECT Lvl FROM dbo.Leaders))
            """);

        stats.SubqueryCount.Should().Be(3);
        stats.MaxSubqueryDepth.Should().Be(2,
            "depth is the nesting level: the innermost IN is two levels below the root statement");
    }

    [Fact]
    public void InList_MaxSizeIsReported()
    {
        var stats = StatsFor("SELECT Id FROM dbo.T WHERE Id IN (1, 2, 3, 4)");

        stats.InListMaxSize.Should().Be(4);
    }

    [Fact]
    public void DistinctUnionOrderByCte_FlagsAreSet()
    {
        var stats = StatsFor(
            """
            WITH t AS (SELECT 1 AS N)
            SELECT DISTINCT N FROM t
            UNION
            SELECT N FROM t
            ORDER BY N
            """);

        stats.HasDistinct.Should().BeTrue();
        stats.HasUnion.Should().BeTrue();
        stats.OrderByCount.Should().Be(1);
        stats.CteCount.Should().Be(1);
    }

    [Fact]
    public void AggregatesAndFunctions_AreCounted()
    {
        var stats = StatsFor(
            "SELECT COUNT(*) AS Cnt, UPPER(Name) AS UName FROM dbo.T GROUP BY Name");

        stats.AggregateCount.Should().Be(1);
        stats.FunctionCount.Should().BeGreaterOrEqualTo(2, "aggregates and scalar functions are both counted");
    }

    [Fact]
    public void SameInput_ProducesStableStatistics()
    {
        const string sql =
            "SELECT c.Id FROM dbo.Customers c WHERE c.Id IN (SELECT CustomerId FROM dbo.Orders) ORDER BY c.Id";

        var first = StatsFor(sql);
        var second = StatsFor(sql);

        first.Should().BeEquivalentTo(second, "statistics are a pure function of the parsed AST");
    }
}
