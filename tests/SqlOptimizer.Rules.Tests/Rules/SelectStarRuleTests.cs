using FluentAssertions;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL001 (SelectStarRule).</summary>
public class SelectStarRuleTests
{
    [Fact]
    public void FlagsPlainStar()
    {
        var findings = RuleTestHelper.Run<SelectStarRule>("SELECT * FROM Customers");

        findings.Should().HaveCount(1);
        findings[0].RuleId.Should().Be("SQL001");
        findings[0].Message.Should().Contain("*");
    }

    [Fact]
    public void FlagsQualifiedStar()
    {
        var findings = RuleTestHelper.Run<SelectStarRule>("SELECT c.* FROM Customers c");

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("c.*");
    }

    [Fact]
    public void DoesNotFlagExplicitColumns()
    {
        var findings = RuleTestHelper.Run<SelectStarRule>("SELECT Id, Name FROM Customers");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagCountStar()
    {
        var findings = RuleTestHelper.Run<SelectStarRule>("SELECT COUNT(*) FROM Customers");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void FlagsStarInsideDerivedTable()
    {
        var findings = RuleTestHelper.Run<SelectStarRule>("SELECT x.Id FROM (SELECT * FROM T) x");

        findings.Should().HaveCount(1);
    }

    [Fact]
    public void FindingHasSemanticDetails()
    {
        var finding = RuleTestHelper.Run<SelectStarRule>("SELECT * FROM Customers")[0];

        finding.Explanation.Should().NotBeNullOrEmpty();
        finding.Recommendations.Should().NotBeEmpty();
        finding.Confidence.Should().BeInRange(0.0, 1.0);
        finding.Impact.Risk.Should().BeLessOrEqualTo(2);
    }
}
