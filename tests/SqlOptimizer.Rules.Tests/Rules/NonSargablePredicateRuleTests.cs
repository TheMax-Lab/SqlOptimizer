using FluentAssertions;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL003 (NonSargablePredicateRule).</summary>
public class NonSargablePredicateRuleTests
{
    [Fact]
    public void FlagsArithmeticOnColumns()
    {
        var findings = RuleTestHelper.Run<NonSargablePredicateRule>(
            "SELECT Id FROM T WHERE Price * Quantity > 100");

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("Price").And.Contain("Quantity");
    }

    [Fact]
    public void FlagsNegatedColumn()
    {
        var findings = RuleTestHelper.Run<NonSargablePredicateRule>(
            "SELECT Id FROM T WHERE -Price = 5");

        findings.Should().HaveCount(1);
        findings[0].Confidence.Should().BeLessThan(0.8);
    }

    [Fact]
    public void DoesNotFlagPlainComparison()
    {
        var findings = RuleTestHelper.Run<NonSargablePredicateRule>(
            "SELECT Id FROM T WHERE Price = 100");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagFunctionWrappedColumns()
    {
        var findings = RuleTestHelper.Run<NonSargablePredicateRule>(
            "SELECT Id FROM T WHERE UPPER(Name) = 'A'");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagArithmeticWithoutColumns()
    {
        var findings = RuleTestHelper.Run<NonSargablePredicateRule>(
            "SELECT Id FROM T WHERE Id = 1 + 1");

        findings.Should().BeEmpty();
    }
}
