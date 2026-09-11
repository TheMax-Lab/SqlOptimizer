using FluentAssertions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Services;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Infrastructure.Parsing;
using Xunit;

namespace SqlOptimizer.Application.Tests.Optimization;

/// <summary>
/// Deterministic candidate generation: only the safe, schema-provable
/// SELECT * expansion produces a candidate; every other shape yields none.
/// Candidates are always created with status Generated — never Validated.
/// </summary>
public class DeterministicCandidateGeneratorTests
{
    private readonly DeterministicCandidateGenerator _generator = new();

    [Fact]
    public async Task StarWithSchema_GeneratesExpansionCandidate()
    {
        var context = BuildContext("SELECT * FROM Customers", OptimizationTestSupport.TestSchema());

        var result = await _generator.GenerateAsync(context);

        result.Candidates.Should().HaveCount(1);
        var candidate = result.Candidates[0];
        candidate.CandidateSql.Should().Be("SELECT Id, Name, City FROM Customers");
        candidate.Source.Should().Be(CandidateSource.Rule);
        candidate.Status.Should().Be(CandidateStatus.Generated);
        candidate.RuleIds.Should().ContainSingle().Which.Should().Be("SQL001");
        candidate.Confidence.Should().BeInRange(0, 1);
        result.Limitations.Should().BeEmpty();
    }

    [Fact]
    public async Task LowercaseStar_ExpandsInOriginalText()
    {
        var context = BuildContext("select * from customers where id > 10", OptimizationTestSupport.TestSchema());

        var result = await _generator.GenerateAsync(context);

        result.Candidates.Should().HaveCount(1);
        result.Candidates[0].CandidateSql.Should().Be("select Id, Name, City from customers where id > 10");
    }

    [Fact]
    public async Task QualifiedStar_ExpandsWithAlias()
    {
        var context = BuildContext("SELECT o.* FROM Orders o", OptimizationTestSupport.TestSchema());

        var result = await _generator.GenerateAsync(context);

        result.Candidates.Should().HaveCount(1);
        result.Candidates[0].CandidateSql.Should().Be("SELECT o.OrderId, o.CustomerId, o.Total FROM Orders o");
    }

    [Fact]
    public async Task StarWithoutSchema_GeneratesNoCandidate()
    {
        var context = BuildContext("SELECT * FROM Customers", null);

        var result = await _generator.GenerateAsync(context);

        result.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task StarOverUnknownTable_GeneratesNoCandidate()
    {
        var context = BuildContext("SELECT * FROM UnknownTable", OptimizationTestSupport.TestSchema());

        var result = await _generator.GenerateAsync(context);

        result.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task StarAmongOtherItems_GeneratesNoCandidate()
    {
        var context = BuildContext("SELECT *, Id FROM Customers", OptimizationTestSupport.TestSchema());

        var result = await _generator.GenerateAsync(context);

        result.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task NoStar_GeneratesNoCandidate()
    {
        var context = BuildContext("SELECT Id FROM Customers", OptimizationTestSupport.TestSchema());

        var result = await _generator.GenerateAsync(context);

        result.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task StarWithJoin_GeneratesNoCandidate()
    {
        var context = BuildContext(
            "SELECT * FROM Customers c INNER JOIN Orders o ON o.CustomerId = c.Id",
            OptimizationTestSupport.TestSchema());

        var result = await _generator.GenerateAsync(context);

        result.Candidates.Should().BeEmpty();
    }

    /// <summary>Parses the query, runs the real rules and builds the context DTO.</summary>
    private static SqlOptimizationContext BuildContext(string sql, DatabaseSchema? schema)
    {
        var parser = new SqlServerSqlParser();
        var parsed = parser.Parse(sql);
        var findings = OptimizationTestSupport.CreateRuleRegistry().Analyze(new SqlAnalysisContext
        {
            Ast = parsed.Root,
            Dialect = SqlDialect.SqlServer,
            Schema = schema
        });

        var analysis = new SqlAnalysis
        {
            Sql = sql,
            Dialect = SqlDialect.SqlServer,
            Ast = parsed.Root,
            ComplexityScore = 10,
            PerformanceScore = 5,
            Findings = findings,
            Statistics = QueryStatistics.FromAst(parsed.Root)
        };

        return new SqlOptimizationContext(
            sql,
            SqlDialect.SqlServer,
            analysis,
            schema,
            null,
            new OptimizationOptions(),
            new Domain.Optimization.OptimizationPlan([]),
            []);
    }
}
