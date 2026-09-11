using FluentAssertions;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL004 (LeadingWildcardLikeRule).</summary>
public class LeadingWildcardLikeRuleTests
{
    [Fact]
    public void FlagsLeadingPercent()
    {
        var findings = RuleTestHelper.Run<LeadingWildcardLikeRule>(
            "SELECT Id FROM T WHERE Name LIKE '%abc'");

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("%abc");
    }

    [Fact]
    public void FlagsBothWildcards()
    {
        var findings = RuleTestHelper.Run<LeadingWildcardLikeRule>(
            "SELECT Id FROM T WHERE Name LIKE '%abc%'");

        findings.Should().HaveCount(1);
    }

    [Fact]
    public void FlagsLeadingUnderscore()
    {
        var findings = RuleTestHelper.Run<LeadingWildcardLikeRule>(
            "SELECT Id FROM T WHERE Name LIKE '_abc'");

        findings.Should().HaveCount(1);
    }

    [Fact]
    public void DoesNotFlagTrailingWildcard()
    {
        var findings = RuleTestHelper.Run<LeadingWildcardLikeRule>(
            "SELECT Id FROM T WHERE Name LIKE 'abc%'");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagExactMatch()
    {
        var findings = RuleTestHelper.Run<LeadingWildcardLikeRule>(
            "SELECT Id FROM T WHERE Name LIKE 'abc'");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagParameterPattern()
    {
        var findings = RuleTestHelper.Run<LeadingWildcardLikeRule>(
            "SELECT Id FROM T WHERE Name LIKE @pattern");

        findings.Should().BeEmpty();
    }
}
