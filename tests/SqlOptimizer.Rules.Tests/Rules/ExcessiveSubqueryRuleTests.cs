using FluentAssertions;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL014 (ExcessiveSubqueryRule).</summary>
public class ExcessiveSubqueryRuleTests
{
    private const string Depth4 =
        "SELECT (SELECT (SELECT (SELECT (SELECT MAX(a) FROM T4) FROM T3) FROM T2) FROM T1) FROM T0";

    [Fact]
    public void DoesNotFlagShallowSubqueries()
    {
        var findings = RuleTestHelper.Run<ExcessiveSubqueryRule>(
            "SELECT Id FROM T WHERE Id = (SELECT MAX(Id) FROM U)");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void FlagsDepthAboveDefaultThreshold()
    {
        var findings = RuleTestHelper.Run<ExcessiveSubqueryRule>(Depth4);

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("4");
    }

    [Fact]
    public void RespectsConfiguredThreshold()
    {
        var findings = RuleTestHelper.Run<ExcessiveSubqueryRule>(
            Depth4, options: new RuleOptions(MaxSubqueryDepth: 5));

        findings.Should().BeEmpty();
    }
}
