using FluentAssertions;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using Xunit;

namespace SqlOptimizer.Domain.Tests.Rules;

/// <summary>
/// Tests for <see cref="RuleRegistry"/>: registration, identifier handling
/// and deterministic rule execution.
/// </summary>
public class RuleRegistryTests
{
    private sealed class TestRule(string id, bool canAnalyze = true, int findings = 1) : SqlOptimizationRuleBase
    {
        public override string Id => id;

        public override string Name => id;

        public override bool CanAnalyze(SqlAnalysisContext context) => canAnalyze;

        public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
        {
            for (var i = 0; i < findings; i++)
            {
                yield return CreateFinding(Severity.Info, FindingCategory.Performance, $"{id}-{i}");
            }
        }
    }

    private static SqlAnalysisContext CreateContext() => new()
    {
        Ast = new SelectStatement([], null, null, [], null, [], false),
        Dialect = SqlOptimizer.Domain.Common.SqlDialect.SqlServer
    };

    [Fact]
    public void Constructor_RejectsDuplicateIdentifiers()
    {
        var rules = new ISqlOptimizationRule[]
        {
            new TestRule("SQL001"),
            new TestRule("sql001")
        };

        var act = () => new RuleRegistry(rules);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FindById_IsCaseInsensitive()
    {
        var registry = new RuleRegistry([new TestRule("SQL001")]);

        registry.FindById("sql001").Should().NotBeNull();
        registry.FindById("SQL001").Should().NotBeNull();
        registry.FindById("SQL999").Should().BeNull();
    }

    [Fact]
    public void Analyze_ExecutesRulesInDeterministicIdOrder()
    {
        var registry = new RuleRegistry(new ISqlOptimizationRule[]
        {
            new TestRule("SQL020"),
            new TestRule("SQL001"),
            new TestRule("SQL010")
        });

        var findings = registry.Analyze(CreateContext());

        findings.Select(f => f.RuleId).Should().Equal(["SQL001", "SQL010", "SQL020"]);
    }

    [Fact]
    public void Analyze_SkipsRulesThatCannotAnalyze()
    {
        var registry = new RuleRegistry(new ISqlOptimizationRule[]
        {
            new TestRule("SQL001", canAnalyze: false),
            new TestRule("SQL002", canAnalyze: true, findings: 2)
        });

        var findings = registry.Analyze(CreateContext());

        findings.Should().HaveCount(2);
        findings.Should().OnlyContain(f => f.RuleId == "SQL002");
    }

    [Fact]
    public void Analyze_EmptyRegistry_ReturnsEmpty()
    {
        var registry = new RuleRegistry(Array.Empty<ISqlOptimizationRule>());

        registry.Analyze(CreateContext()).Should().BeEmpty();
    }

    [Fact]
    public void Analyze_ReturnsSameFindingsOnRepeatedCalls()
    {
        var registry = new RuleRegistry([new TestRule("SQL001", findings: 3)]);

        var first = registry.Analyze(CreateContext());
        var second = registry.Analyze(CreateContext());

        first.Select(f => f.Message).Should().BeEquivalentTo(second.Select(f => f.Message));
    }
}
