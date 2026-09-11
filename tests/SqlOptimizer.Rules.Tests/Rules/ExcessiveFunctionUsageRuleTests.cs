using FluentAssertions;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL020 (ExcessiveFunctionUsageRule).</summary>
public class ExcessiveFunctionUsageRuleTests
{
    [Fact]
    public void DoesNotFlagBelowDefaultThreshold()
    {
        var findings = RuleTestHelper.Run<ExcessiveFunctionUsageRule>(
            "SELECT UPPER(a), LOWER(b), TRIM(c) FROM T");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void FlagsAtDefaultThreshold()
    {
        var findings = RuleTestHelper.Run<ExcessiveFunctionUsageRule>(
            "SELECT UPPER(a), LOWER(b), TRIM(c), LTRIM(d), RTRIM(e) FROM T");

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("5");
    }

    [Fact]
    public void RespectsConfiguredThreshold()
    {
        var findings = RuleTestHelper.Run<ExcessiveFunctionUsageRule>(
            "SELECT UPPER(a), LOWER(b) FROM T",
            options: new RuleOptions(ExcessiveFunctionThreshold: 2));

        findings.Should().HaveCount(1);
    }

    [Fact]
    public void DoesNotCountAggregates()
    {
        var findings = RuleTestHelper.Run<ExcessiveFunctionUsageRule>(
            "SELECT COUNT(*), SUM(x), AVG(y), MIN(z), MAX(w) FROM T");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void FindingIsReviewFlagNotDiagnosis()
    {
        var finding = RuleTestHelper.Run<ExcessiveFunctionUsageRule>(
            "SELECT UPPER(a), LOWER(b), TRIM(c), LTRIM(d), RTRIM(e) FROM T")[0];

        finding.Confidence.Should().BeLessThan(0.6);
        finding.Explanation.Should().Contain("review flag");
    }
}
