using FluentAssertions;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Rules.Scoring;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Scoring;

/// <summary>
/// Tests for <see cref="DeterministicScoreEngine"/>: the scoring must be
/// pure, bounded and explainable.
/// </summary>
public class DeterministicScoreEngineTests
{
    private readonly DeterministicScoreEngine _engine = new();

    private static SqlFinding Finding(Severity severity, double confidence, int performanceImpact = 0) => new()
    {
        RuleId = "SQL000",
        Severity = severity,
        Category = FindingCategory.Performance,
        Message = "test",
        Confidence = confidence,
        Impact = new OptimizationImpact(performanceImpact, 0, 0, 0),
    };

    [Fact]
    public void SimpleQueryScoresLowOnBothDimensions()
    {
        var ast = new SelectStatement(
            [new SelectItem(new ColumnExpression("Id"), null)],
            new FromClause(new TableReference("", "T", null)),
            null, [], null, [], false);

        var result = _engine.Score(ast, []);

        result.ComplexityScore.Should().BeInRange(0, 20);
        result.PerformanceScore.Should().Be(0);
    }

    [Fact]
    public void ComplexityGrowsWithStructure()
    {
        var simple = new SelectStatement(
            [new SelectItem(new ColumnExpression("Id"), null)],
            new FromClause(new TableReference("", "T", null)),
            null, [], null, [], false);

        var inner = new SelectStatement(
            [new SelectItem(new ColumnExpression("X"), null)],
            new FromClause(new JoinSource(
                new TableReference("", "A", "a"),
                new TableReference("", "B", "b"),
                JoinType.Inner,
                new BinaryExpression(SqlBinaryOperator.Equal, new ColumnExpression("A", "a"), new ColumnExpression("B", "b")))),
            null, [], null, [], false);

        var complex = new SelectStatement(
            [new SelectItem(new SubqueryExpression(inner), null)],
            new FromClause(new TableReference("", "T", null)),
            null, [], null,
            [new OrderByItem(new ColumnExpression("Id"), true)],
            true);

        var simpleScore = _engine.Score(simple, []).ComplexityScore;
        var complexScore = _engine.Score(complex, []).ComplexityScore;

        complexScore.Should().BeGreaterThan(simpleScore);
        complexScore.Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public void PerformanceIsZeroWithoutFindings()
    {
        var ast = new SelectStatement(
            [new SelectItem(new ColumnExpression("Id"), null)],
            new FromClause(new TableReference("", "T", null)),
            null, [], null, [], false);

        _engine.Score(ast, []).PerformanceScore.Should().Be(0);
    }

    [Fact]
    public void PerformanceGrowsWithFindingsAndSeverity()
    {
        var ast = new SelectStatement([], null, null, [], null, [], false);

        var none = _engine.Score(ast, []).PerformanceScore;
        var info = _engine.Score(ast, [Finding(Severity.Info, 0.5)]).PerformanceScore;
        var warning = _engine.Score(ast, [Finding(Severity.Warning, 0.5, 5)]).PerformanceScore;
        var high = _engine.Score(ast, [Finding(Severity.High, 0.8, 6)]).PerformanceScore;
        var critical = _engine.Score(ast, [Finding(Severity.Critical, 0.9, 8)]).PerformanceScore;

        info.Should().BeGreaterThan(none);
        warning.Should().BeGreaterThan(info);
        high.Should().BeGreaterThan(warning);
        critical.Should().BeGreaterThan(high);
    }

    [Fact]
    public void ScoresAreDeterministicAcrossRepeatedCalls()
    {
        var ast = new SelectStatement(
            [new SelectItem(new ColumnExpression("Id"), null)],
            new FromClause(new TableReference("", "T", null)),
            null, [], null, [], false);

        var findings = new[]
        {
            Finding(Severity.Warning, 0.7, 4),
            Finding(Severity.High, 0.8, 6),
        };

        var first = _engine.Score(ast, findings);
        var second = _engine.Score(ast, findings);

        first.Should().Be(second);
    }

    [Fact]
    public void ScoresAreAlwaysBounded()
    {
        var deepest = new SelectStatement(
            [new SelectItem(new ColumnExpression("Z"), null)],
            new FromClause(new JoinSource(
                new TableReference("", "A", "a"),
                new TableReference("", "B", "b"),
                JoinType.Inner,
                new BinaryExpression(SqlBinaryOperator.Equal, new ColumnExpression("A", "a"), new ColumnExpression("B", "b")))),
            null, [], null, [], false);

        var depth2 = new SelectStatement(
            [new SelectItem(new SubqueryExpression(deepest), null)],
            new FromClause(new TableReference("", "C", null)),
            null, [], null, [], false);

        var depth3 = new SelectStatement(
            [new SelectItem(new SubqueryExpression(depth2), null)],
            new FromClause(new TableReference("", "D", null)),
            null, [], null, [], false);

        var ast = new SelectStatement(
            [new SelectItem(new SubqueryExpression(depth3), null)],
            new FromClause(new JoinSource(
                new TableReference("", "E", "e"),
                new TableReference("", "F", "f"),
                JoinType.Inner,
                new BinaryExpression(SqlBinaryOperator.Equal, new ColumnExpression("E", "e"), new ColumnExpression("F", "f")))),
            null, [], null, [], true);

        var findings = Enumerable.Range(0, 30)
            .Select(_ => Finding(Severity.Critical, 1.0, 10))
            .ToList();

        var result = _engine.Score(ast, findings);

        result.ComplexityScore.Should().BeInRange(0, 100);
        result.PerformanceScore.Should().BeInRange(0, 100);
    }
}
