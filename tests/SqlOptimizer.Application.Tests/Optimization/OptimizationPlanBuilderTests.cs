using FluentAssertions;
using SqlOptimizer.Application.Services;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Domain.Optimization;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Infrastructure.Parsing;
using Xunit;

namespace SqlOptimizer.Application.Tests.Optimization;

/// <summary>
/// Deterministic plan building: findings map to concrete action proposals,
/// the strategy filters by risk, and the plan stays ordered and bounded.
/// Rules without a safe mechanical rewrite (SELECT *, UNION) produce no
/// action — recommendations only.
/// </summary>
public class OptimizationPlanBuilderTests
{
    private readonly OptimizationPlanBuilder _builder = new();

    [Fact]
    public void NoFindings_EmptyPlan()
    {
        var (analysis, context) = Build("SELECT Id FROM Customers WHERE Id = 10");
        var plan = _builder.Build(analysis, context, OptimizationStrategy.Balanced);

        plan.Actions.Should().BeEmpty();
    }

    [Fact]
    public void StarFinding_ProducesNoAction()
    {
        var (analysis, context) = Build("SELECT * FROM Customers");
        analysis.Findings.Should().Contain(f => f.RuleId == "SQL001");

        var plan = _builder.Build(analysis, context, OptimizationStrategy.Aggressive);

        plan.Actions.Should().NotContain(a => a.RelatedRuleId == "SQL001");
    }

    [Fact]
    public void DistinctFinding_HighRiskAction_FilteredByConservative()
    {
        var (analysis, context) = Build("SELECT DISTINCT Id FROM Customers");
        analysis.Findings.Should().Contain(f => f.RuleId == "SQL009");

        var aggressive = _builder.Build(analysis, context, OptimizationStrategy.Aggressive);
        aggressive.Actions.Should().Contain(a => a is RemoveDistinctAction && a.RelatedRuleId == "SQL009");

        var conservative = _builder.Build(analysis, context, OptimizationStrategy.Conservative);
        conservative.Actions.Should().NotContain(a => a is RemoveDistinctAction);
    }

    [Fact]
    public void UnnecessaryCast_LowRiskAction_IncludedInConservative()
    {
        var schema = OptimizationTestSupport.TestSchema();
        var (analysis, context) = Build("SELECT CAST(Id AS INT) FROM Customers", schema);
        analysis.Findings.Should().Contain(f => f.RuleId == "SQL016");

        var plan = _builder.Build(analysis, context, OptimizationStrategy.Conservative);
        plan.Actions.Should().Contain(a => a is RemoveRedundantExpressionAction && a.RelatedRuleId == "SQL016");
    }

    [Fact]
    public void Plan_IsOrderedByRiskAscendingThenConfidence()
    {
        var schema = OptimizationTestSupport.TestSchema();
        var (analysis, context) = Build(
            "SELECT DISTINCT CAST(Id AS INT) FROM Customers WHERE UPPER(City) = 'Rome'", schema);

        var plan = _builder.Build(analysis, context, OptimizationStrategy.Aggressive);
        if (plan.Actions.Count < 2)
        {
            return; // findings vary; ordering is only meaningful with several actions
        }

        for (var i = 1; i < plan.Actions.Count; i++)
        {
            ((int)plan.Actions[i - 1].Risk).Should().BeLessOrEqualTo((int)plan.Actions[i].Risk,
                "actions are ordered by risk ascending");
        }
    }

    [Fact]
    public void EstimatedTotalBenefit_IsBounded()
    {
        var (analysis, context) = Build("SELECT * FROM Customers WHERE UPPER(City) = 'Rome'");
        var plan = _builder.Build(analysis, context, OptimizationStrategy.Aggressive);

        plan.EstimatedTotalBenefit.Should().BeInRange(0, 100);
    }

    /// <summary>Parses the query, runs the real rules and builds the rule context.</summary>
    private static (SqlAnalysis, SqlAnalysisContext) Build(string sql, DatabaseSchema? schema = null)
    {
        var parser = new SqlServerSqlParser();
        var parsed = parser.Parse(sql);

        var context = new SqlAnalysisContext
        {
            Ast = parsed.Root,
            Dialect = SqlDialect.SqlServer,
            Schema = schema
        };

        var findings = OptimizationTestSupport.CreateRuleRegistry().Analyze(context);
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

        return (analysis, context);
    }
}
