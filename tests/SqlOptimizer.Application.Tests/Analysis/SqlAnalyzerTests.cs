using FluentAssertions;
using SqlOptimizer.Application.Services;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Infrastructure.Parsing;
using SqlOptimizer.Rules;
using SqlOptimizer.Rules.Scoring;
using Xunit;

namespace SqlOptimizer.Application.Tests.Analysis;

/// <summary>
/// End-to-end tests of the deterministic analysis pipeline:
/// SQL → parser → AST → context → rules → findings → scores. All components
/// are the production implementations; no mocks are used.
/// </summary>
public class SqlAnalyzerTests
{
    /// <summary>Creates the full production pipeline (parser, 20 rules, score engine).</summary>
    internal static SqlAnalyzer CreateAnalyzer() => new(
        new SqlServerSqlParser(),
        new RuleRegistry(new ISqlOptimizationRule[]
        {
            new SelectStarRule(),
            new FunctionOnColumnRule(),
            new NonSargablePredicateRule(),
            new LeadingWildcardLikeRule(),
            new ImplicitConversionRule(),
            new OrPredicateRule(),
            new CorrelatedSubqueryRule(),
            new NotInNullableRule(),
            new DistinctRule(),
            new UnionRule(),
            new RedundantOrderByRule(),
            new CartesianJoinRule(),
            new LeftJoinFilterRule(),
            new ExcessiveSubqueryRule(),
            new LargeInListRule(),
            new UnnecessaryCastRule(),
            new DuplicateExpressionRule(),
            new PotentialJoinExplosionRule(),
            new MissingJoinPredicateRule(),
            new ExcessiveFunctionUsageRule()
        }),
        new DeterministicScoreEngine(),
        new SqlOptimizer.Application.Options.SqlOptimizerOptions());

    private static Task<SqlOptimizer.Domain.Analysis.SqlAnalysis> AnalyzeAsync(
        string sql,
        SqlDialect dialect = SqlDialect.SqlServer,
        DatabaseSchema? schema = null) =>
        CreateAnalyzer().AnalyzeAsync(new SqlOptimizer.Application.DTOs.SqlAnalysisRequest
        {
            Sql = sql,
            Dialect = dialect,
            Schema = schema
        });

    // 1. SELECT * FROM Customers
    [Fact]
    public async Task SelectStar_ProducesFindingAndScores()
    {
        var analysis = await AnalyzeAsync("SELECT * FROM Customers");

        analysis.Findings.Should().Contain(f => f.RuleId == "SQL001");
        analysis.PerformanceScore.Should().BeGreaterThan(0);
        analysis.ComplexityScore.Should().BeInRange(0, 100);
        analysis.Statistics.TableCount.Should().Be(1);
    }

    // 2. SELECT c.Id, c.Name FROM Customers c WHERE LOWER(c.Name) = 'john'
    [Fact]
    public async Task FunctionOnColumn_ProducesSargabilityFinding()
    {
        var analysis = await AnalyzeAsync(
            "SELECT c.Id, c.Name FROM Customers c WHERE LOWER(c.Name) = 'john'");

        var finding = analysis.Findings.Should().ContainSingle(f => f.RuleId == "SQL002").Subject;
        finding.Message.Should().Contain("LOWER").And.Contain("Name");
        analysis.Findings.Should().NotContain(f => f.RuleId == "SQL001");
    }

    // 3. SELECT * FROM Orders WHERE YEAR(OrderDate) = 2025
    [Fact]
    public async Task YearOnColumn_ProducesStarAndFunctionFindings()
    {
        var analysis = await AnalyzeAsync("SELECT * FROM Orders WHERE YEAR(OrderDate) = 2025");

        analysis.Findings.Should()
            .Contain(f => f.RuleId == "SQL001")
            .And.Contain(f => f.RuleId == "SQL002");
    }

    // 4. Correlated EXISTS
    [Fact]
    public async Task CorrelatedExists_ProducesFinding()
    {
        var analysis = await AnalyzeAsync(
            "SELECT c.Id FROM Customers c WHERE EXISTS (SELECT 1 FROM Orders o WHERE o.CustomerId = c.Id)");

        analysis.Findings.Should().Contain(f => f.RuleId == "SQL007");
    }

    // 5. CROSS JOIN
    [Fact]
    public async Task CrossJoin_ProducesFinding()
    {
        var analysis = await AnalyzeAsync("SELECT a.X, b.Y FROM A a CROSS JOIN B b");

        analysis.Findings.Should().Contain(f => f.RuleId == "SQL012");
    }

    // 6. LEFT JOIN with a WHERE predicate on the right-side table
    [Fact]
    public async Task LeftJoinWithWhereFilter_ProducesFinding()
    {
        var analysis = await AnalyzeAsync(
            "SELECT c.Id FROM Customers c LEFT JOIN Orders o ON o.CustomerId = c.Id WHERE o.Status = 'Paid'");

        analysis.Findings.Should().Contain(f => f.RuleId == "SQL013");
    }

    // 7. DISTINCT with JOIN
    [Fact]
    public async Task DistinctWithJoin_ProducesFinding()
    {
        var analysis = await AnalyzeAsync(
            "SELECT DISTINCT c.Id FROM Customers c JOIN Orders o ON o.CustomerId = c.Id");

        analysis.Findings.Should().Contain(f => f.RuleId == "SQL009");
    }

    // 8. UNION versus UNION ALL
    [Fact]
    public async Task Union_ProducesFinding_UnionAllDoesNot()
    {
        var union = await AnalyzeAsync("SELECT Id FROM A UNION SELECT Id FROM B");
        var unionAll = await AnalyzeAsync("SELECT Id FROM A UNION ALL SELECT Id FROM B");

        union.Findings.Should().Contain(f => f.RuleId == "SQL010");
        unionAll.Findings.Should().NotContain(f => f.RuleId == "SQL010");
    }
}
