using FluentAssertions;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL007 (CorrelatedSubqueryRule).</summary>
public class CorrelatedSubqueryRuleTests
{
    [Fact]
    public void FlagsCorrelatedExists()
    {
        var findings = RuleTestHelper.Run<CorrelatedSubqueryRule>(
            "SELECT c.Id FROM Customers c WHERE EXISTS (SELECT 1 FROM Orders o WHERE o.CustomerId = c.Id)");

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("c");
    }

    [Fact]
    public void FlagsCorrelatedScalarSubquery()
    {
        var findings = RuleTestHelper.Run<CorrelatedSubqueryRule>(
            "SELECT (SELECT MAX(o.Total) FROM Orders o WHERE o.CustomerId = c.Id) FROM Customers c");

        findings.Should().HaveCount(1);
    }

    [Fact]
    public void DoesNotFlagUncorrelatedExists()
    {
        var findings = RuleTestHelper.Run<CorrelatedSubqueryRule>(
            "SELECT c.Id FROM Customers c WHERE EXISTS (SELECT 1 FROM Orders)");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagUnqualifiedOuterReferences()
    {
        var findings = RuleTestHelper.Run<CorrelatedSubqueryRule>(
            "SELECT Id FROM Customers WHERE EXISTS (SELECT 1 FROM Orders WHERE CustomerId = Id)");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void FindingDoesNotAssertJoinRewrite()
    {
        var finding = RuleTestHelper.Run<CorrelatedSubqueryRule>(
            "SELECT c.Id FROM Customers c WHERE EXISTS (SELECT 1 FROM Orders o WHERE o.CustomerId = c.Id)")[0];

        finding.Recommendations.Should().NotContain(r => r.StartsWith("Rewrite as JOIN", StringComparison.OrdinalIgnoreCase));
        finding.Explanation.Should().Contain("EXISTS");
    }
}
