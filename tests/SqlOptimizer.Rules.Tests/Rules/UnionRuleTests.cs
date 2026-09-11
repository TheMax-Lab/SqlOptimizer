using FluentAssertions;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL010 (UnionRule).</summary>
public class UnionRuleTests
{
    [Fact]
    public void FlagsUnion()
    {
        var findings = RuleTestHelper.Run<UnionRule>(
            "SELECT Id FROM A UNION SELECT Id FROM B");

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("UNION");
    }

    [Fact]
    public void DoesNotFlagUnionAll()
    {
        var findings = RuleTestHelper.Run<UnionRule>(
            "SELECT Id FROM A UNION ALL SELECT Id FROM B");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagPlainSelect()
    {
        var findings = RuleTestHelper.Run<UnionRule>("SELECT Id FROM A");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void FindingExplainsDuplicateSemantics()
    {
        var finding = RuleTestHelper.Run<UnionRule>(
            "SELECT Id FROM A UNION SELECT Id FROM B")[0];

        finding.Confidence.Should().BeLessThan(0.5);
        finding.Explanation.Should().Contain("UNION");
        finding.Explanation.Should().Contain("duplicate");
    }
}
