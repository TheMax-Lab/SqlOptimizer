using FluentAssertions;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL015 (LargeInListRule).</summary>
public class LargeInListRuleTests
{
    private static string InListWith(int count) =>
        $"SELECT Id FROM T WHERE Id IN ({string.Join(", ", Enumerable.Range(1, count))})";

    [Fact]
    public void DoesNotFlagBelowDefaultThreshold()
    {
        var findings = RuleTestHelper.Run<LargeInListRule>(InListWith(10));

        findings.Should().BeEmpty();
    }

    [Fact]
    public void FlagsAtDefaultThreshold()
    {
        var findings = RuleTestHelper.Run<LargeInListRule>(InListWith(20));

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("20");
    }

    [Fact]
    public void RespectsConfiguredThreshold()
    {
        var findings = RuleTestHelper.Run<LargeInListRule>(
            InListWith(5), options: new RuleOptions(LargeInListThreshold: 3));

        findings.Should().HaveCount(1);
    }

    [Fact]
    public void DoesNotFlagInSubquery()
    {
        var findings = RuleTestHelper.Run<LargeInListRule>(
            "SELECT Id FROM T WHERE Id IN (SELECT Id FROM U)");

        findings.Should().BeEmpty();
    }
}
