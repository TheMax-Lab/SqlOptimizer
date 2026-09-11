using FluentAssertions;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL009 (DistinctRule).</summary>
public class DistinctRuleTests
{
    [Fact]
    public void FlagsDistinct()
    {
        var findings = RuleTestHelper.Run<DistinctRule>("SELECT DISTINCT Id FROM T");

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("DISTINCT");
    }

    [Fact]
    public void DoesNotFlagWithoutDistinct()
    {
        var findings = RuleTestHelper.Run<DistinctRule>("SELECT Id FROM T");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void HigherConfidenceWhenJoinsArePresent()
    {
        var withoutJoin = RuleTestHelper.Run<DistinctRule>("SELECT DISTINCT Id FROM T")[0];
        var withJoin = RuleTestHelper.Run<DistinctRule>(
            "SELECT DISTINCT c.Id FROM Customers c JOIN Orders o ON o.CustomerId = c.Id")[0];

        withJoin.Confidence.Should().BeGreaterThan(withoutJoin.Confidence);
    }

    [Fact]
    public void FindingDoesNotRecommendAutomaticRemoval()
    {
        var finding = RuleTestHelper.Run<DistinctRule>("SELECT DISTINCT Id FROM T")[0];

        finding.Severity.Should().Be(SqlOptimizer.Domain.Analysis.Severity.Info);
        finding.Recommendations.Should().NotContain(r => r.Contains("Remove SELECT DISTINCT"));
        finding.Explanation.Should().Contain("unique rows");
    }
}
