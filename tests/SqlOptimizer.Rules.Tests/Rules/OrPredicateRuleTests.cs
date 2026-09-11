using FluentAssertions;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL006 (OrPredicateRule).</summary>
public class OrPredicateRuleTests
{
    [Fact]
    public void FlagsOrInWhere()
    {
        var findings = RuleTestHelper.Run<OrPredicateRule>(
            "SELECT Id FROM T WHERE A = 1 OR B = 2");

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("OR");
    }

    [Fact]
    public void FlagsOrUnderAnd()
    {
        var findings = RuleTestHelper.Run<OrPredicateRule>(
            "SELECT Id FROM T WHERE A = 1 AND (B = 2 OR C = 3)");

        findings.Should().HaveCount(1);
    }

    [Fact]
    public void DoesNotFlagAndOnlyPredicates()
    {
        var findings = RuleTestHelper.Run<OrPredicateRule>(
            "SELECT Id FROM T WHERE A = 1 AND B = 2");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void FindingExplainsThatOrIsNotAlwaysBad()
    {
        var finding = RuleTestHelper.Run<OrPredicateRule>(
            "SELECT Id FROM T WHERE A = 1 OR B = 2")[0];

        finding.Severity.Should().Be(SqlOptimizer.Domain.Analysis.Severity.Info);
        finding.Confidence.Should().BeLessThan(0.6);
        finding.Explanation.Should().Contain("selectivity");
    }
}
