using FluentAssertions;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL002 (FunctionOnColumnRule).</summary>
public class FunctionOnColumnRuleTests
{
    [Fact]
    public void FlagsYearFunctionOnColumn()
    {
        var findings = RuleTestHelper.Run<FunctionOnColumnRule>(
            "SELECT Id FROM Orders WHERE YEAR(OrderDate) = 2025");

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("YEAR").And.Contain("OrderDate");
    }

    [Fact]
    public void FlagsLowerFunctionOnColumn()
    {
        var findings = RuleTestHelper.Run<FunctionOnColumnRule>(
            "SELECT Id FROM Customers WHERE LOWER(Name) = 'john'");

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("LOWER").And.Contain("Name");
    }

    [Fact]
    public void FlagsCastOnColumn()
    {
        var findings = RuleTestHelper.Run<FunctionOnColumnRule>(
            "SELECT Id FROM Orders WHERE CAST(OrderDate AS DATE) = '2025-01-01'");

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("CAST AS DATE").And.Contain("OrderDate");
    }

    [Fact]
    public void DoesNotFlagFunctionOnParameter()
    {
        var findings = RuleTestHelper.Run<FunctionOnColumnRule>(
            "SELECT Id FROM Customers WHERE Name = LOWER(@name)");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagFunctionWithoutColumn()
    {
        var findings = RuleTestHelper.Run<FunctionOnColumnRule>(
            "SELECT Id FROM Orders WHERE OrderDate = GETDATE()");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagAggregatesInHaving()
    {
        var findings = RuleTestHelper.Run<FunctionOnColumnRule>(
            "SELECT GroupKey FROM T GROUP BY GroupKey HAVING COUNT(GroupKey) > 1");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void ConfidenceIsHigherWithMetadata()
    {
        var withoutMetadata = RuleTestHelper.Run<FunctionOnColumnRule>(
            "SELECT Id FROM T WHERE LOWER(Name) = 'john'")[0];

        var withMetadata = RuleTestHelper.Run<FunctionOnColumnRule>(
            "SELECT Id FROM T WHERE LOWER(Name) = 'john'",
            RuleTestHelper.BuildSchema(("Name", "NVARCHAR(100)", true, false)))[0];

        withMetadata.Confidence.Should().BeGreaterThan(withoutMetadata.Confidence);
    }
}
