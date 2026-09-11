using FluentAssertions;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL013 (LeftJoinFilterRule).</summary>
public class LeftJoinFilterRuleTests
{
    [Fact]
    public void FlagsWhereFilterOnOuterJoinedSide()
    {
        var findings = RuleTestHelper.Run<LeftJoinFilterRule>(
            "SELECT c.Id FROM Customers c LEFT JOIN Orders o ON o.CustomerId = c.Id WHERE o.Status = 'Paid'");

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("LEFT JOIN");
    }

    [Fact]
    public void FlagsIsNotNullFilter()
    {
        var findings = RuleTestHelper.Run<LeftJoinFilterRule>(
            "SELECT c.Id FROM Customers c LEFT JOIN Orders o ON o.CustomerId = c.Id WHERE o.Id IS NOT NULL");

        findings.Should().HaveCount(1);
    }

    [Fact]
    public void DoesNotFlagIsNullAntiJoinFilter()
    {
        var findings = RuleTestHelper.Run<LeftJoinFilterRule>(
            "SELECT c.Id FROM Customers c LEFT JOIN Orders o ON o.CustomerId = c.Id WHERE o.Id IS NULL");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void FlagsFilterOnNullableSideOfRightJoin()
    {
        var findings = RuleTestHelper.Run<LeftJoinFilterRule>(
            "SELECT c.Id FROM Customers c RIGHT JOIN Orders o ON o.CustomerId = c.Id WHERE c.City = 'Rome'");

        // For a RIGHT JOIN the nullable side is the LEFT table (c), so a
        // filter on c is flagged.
        findings.Should().HaveCount(1);
    }

    [Fact]
    public void DoesNotFlagFilterOnPreservedSideOfLeftJoin()
    {
        var findings = RuleTestHelper.Run<LeftJoinFilterRule>(
            "SELECT c.Id FROM Customers c LEFT JOIN Orders o ON o.CustomerId = c.Id WHERE c.City = 'Rome'");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagInnerJoinFilters()
    {
        var findings = RuleTestHelper.Run<LeftJoinFilterRule>(
            "SELECT c.Id FROM Customers c JOIN Orders o ON o.CustomerId = c.Id WHERE o.Status = 'Paid'");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void FindingExplainsBothIntendedBehaviors()
    {
        var finding = RuleTestHelper.Run<LeftJoinFilterRule>(
            "SELECT c.Id FROM Customers c LEFT JOIN Orders o ON o.CustomerId = c.Id WHERE o.Status = 'Paid'")[0];

        finding.Explanation.Should().Contain("INNER JOIN").And.Contain("IS NULL");
        finding.Recommendations.Should().NotContain(r => r.Contains("automatically"));
    }
}
