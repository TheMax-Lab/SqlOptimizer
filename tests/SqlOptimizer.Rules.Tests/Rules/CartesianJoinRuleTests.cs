using FluentAssertions;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL012 (CartesianJoinRule).</summary>
public class CartesianJoinRuleTests
{
    [Fact]
    public void FlagsCrossJoin()
    {
        var findings = RuleTestHelper.Run<CartesianJoinRule>(
            "SELECT a.X, b.Y FROM A a CROSS JOIN B b");

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("CROSS JOIN");
    }

    [Fact]
    public void FlagsCommaFromAsCartesian()
    {
        var findings = RuleTestHelper.Run<CartesianJoinRule>(
            "SELECT a.X FROM A a, B b");

        findings.Should().HaveCount(1);
    }

    [Fact]
    public void FlagsConstantOnPredicate()
    {
        var findings = RuleTestHelper.Run<CartesianJoinRule>(
            "SELECT a.X FROM A a JOIN B b ON 1 = 1");

        findings.Should().HaveCount(1);
        findings[0].Confidence.Should().BeGreaterOrEqualTo(0.8);
    }

    [Fact]
    public void DoesNotFlagProperInnerJoin()
    {
        var findings = RuleTestHelper.Run<CartesianJoinRule>(
            "SELECT a.X FROM A a JOIN B b ON a.Id = b.A");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagProperLeftJoin()
    {
        var findings = RuleTestHelper.Run<CartesianJoinRule>(
            "SELECT a.X FROM A a LEFT JOIN B b ON a.Id = b.A");

        findings.Should().BeEmpty();
    }
}
