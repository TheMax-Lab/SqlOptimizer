using FluentAssertions;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL011 (RedundantOrderByRule).</summary>
public class RedundantOrderByRuleTests
{
    [Fact]
    public void FlagsDuplicateOrderByItems()
    {
        var findings = RuleTestHelper.Run<RedundantOrderByRule>(
            "SELECT Id, Name FROM T ORDER BY Id, Id");

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("Id");
    }

    [Fact]
    public void DoesNotFlagDistinctItems()
    {
        var findings = RuleTestHelper.Run<RedundantOrderByRule>(
            "SELECT Id, Name FROM T ORDER BY Id, Name");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagSameColumnDifferentDirection()
    {
        var findings = RuleTestHelper.Run<RedundantOrderByRule>(
            "SELECT Id, Name FROM T ORDER BY Id ASC, Id DESC");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void FindingKeepsRemainingOrdering()
    {
        var finding = RuleTestHelper.Run<RedundantOrderByRule>(
            "SELECT Id, Name FROM T ORDER BY Id, Id")[0];

        finding.Severity.Should().Be(SqlOptimizer.Domain.Analysis.Severity.Info);
        finding.Impact.Risk.Should().Be(0);
    }
}
