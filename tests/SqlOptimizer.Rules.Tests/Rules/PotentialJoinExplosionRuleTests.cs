using FluentAssertions;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL018 (PotentialJoinExplosionRule).</summary>
public class PotentialJoinExplosionRuleTests
{
    private static DatabaseSchema TwoTablesWithNonUniqueKeys() =>
        new([
            new DatabaseTable("dbo", "A", null,
            [
                new DatabaseColumn("Id", "INT", false, true),
                new DatabaseColumn("X", "INT", true, false)
            ], []),
            new DatabaseTable("dbo", "B", null,
            [
                new DatabaseColumn("Id", "INT", false, true),
                new DatabaseColumn("Y", "INT", true, false)
            ], [])
        ]);

    [Fact]
    public void CanAnalyze_IsFalseWithoutSchema()
    {
        var rule = new PotentialJoinExplosionRule();

        rule.CanAnalyze(RuleTestHelper.BuildContext("SELECT a.X FROM A a JOIN B b ON a.X = b.Y"))
            .Should().BeFalse();
    }

    [Fact]
    public void FlagsJoinOnNonUniqueColumns()
    {
        var findings = RuleTestHelper.Run<PotentialJoinExplosionRule>(
            "SELECT a.X FROM A a JOIN B b ON a.X = b.Y",
            TwoTablesWithNonUniqueKeys());

        findings.Should().HaveCount(1);
        findings[0].Category.Should().Be(FindingCategory.Cardinality);
    }

    [Fact]
    public void DoesNotFlagJoinOnUniqueKey()
    {
        var findings = RuleTestHelper.Run<PotentialJoinExplosionRule>(
            "SELECT a.X FROM A a JOIN B b ON a.Id = b.Y",
            TwoTablesWithNonUniqueKeys());

        findings.Should().BeEmpty();
    }

    [Fact]
    public void HigherConfidenceWhenRowCountsAreKnown()
    {
        var withRowCounts = new DatabaseSchema([
            new DatabaseTable("dbo", "A", 1000,
            [
                new DatabaseColumn("Id", "INT", false, true),
                new DatabaseColumn("X", "INT", true, false)
            ], []),
            new DatabaseTable("dbo", "B", 2000,
            [
                new DatabaseColumn("Id", "INT", false, true),
                new DatabaseColumn("Y", "INT", true, false)
            ], [])
        ]);

        var withoutRowCounts = RuleTestHelper.Run<PotentialJoinExplosionRule>(
            "SELECT a.X FROM A a JOIN B b ON a.X = b.Y", TwoTablesWithNonUniqueKeys())[0];
        var withRowCountsFinding = RuleTestHelper.Run<PotentialJoinExplosionRule>(
            "SELECT a.X FROM A a JOIN B b ON a.X = b.Y", withRowCounts)[0];

        withRowCountsFinding.Confidence.Should().BeGreaterThan(withoutRowCounts.Confidence);
        withRowCountsFinding.Message.Should().Contain("1,000").And.Contain("2,000");
    }

    [Fact]
    public void DoesNotFlagCrossJoins()
    {
        var findings = RuleTestHelper.Run<PotentialJoinExplosionRule>(
            "SELECT a.X FROM A a CROSS JOIN B b",
            TwoTablesWithNonUniqueKeys());

        findings.Should().BeEmpty();
    }

    [Fact]
    public void FindingDoesNotClaimCertainty()
    {
        var finding = RuleTestHelper.Run<PotentialJoinExplosionRule>(
            "SELECT a.X FROM A a JOIN B b ON a.X = b.Y",
            TwoTablesWithNonUniqueKeys())[0];

        finding.Confidence.Should().BeLessThan(0.7);
        finding.Explanation.Should().Contain("risk flag");
    }
}
