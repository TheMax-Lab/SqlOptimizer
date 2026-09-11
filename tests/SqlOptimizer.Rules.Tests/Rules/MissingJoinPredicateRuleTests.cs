using FluentAssertions;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL019 (MissingJoinPredicateRule).</summary>
public class MissingJoinPredicateRuleTests
{
    [Fact]
    public void FlagsConstantOnPredicate()
    {
        var findings = RuleTestHelper.Run<MissingJoinPredicateRule>(
            "SELECT a.X FROM A a JOIN B b ON 1 = 1");

        findings.Should().HaveCount(1);
        findings[0].Severity.Should().Be(Severity.Critical);
    }

    [Fact]
    public void FlagsPredicateReferencingOnlyOneSide()
    {
        var findings = RuleTestHelper.Run<MissingJoinPredicateRule>(
            "SELECT a.X FROM A a JOIN B b ON b.Status = 'Paid'");

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("both sides");
    }

    [Fact]
    public void DoesNotFlagCrossJoin()
    {
        var findings = RuleTestHelper.Run<MissingJoinPredicateRule>(
            "SELECT a.X FROM A a CROSS JOIN B b");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagProperJoin()
    {
        var findings = RuleTestHelper.Run<MissingJoinPredicateRule>(
            "SELECT a.X FROM A a JOIN B b ON a.Id = b.A");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void SkipsPredicatesWithUnqualifiedColumns()
    {
        var findings = RuleTestHelper.Run<MissingJoinPredicateRule>(
            "SELECT a.X FROM A a JOIN B b ON Status = 'Paid'");

        findings.Should().BeEmpty();
    }
}
