using FluentAssertions;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL017 (DuplicateExpressionRule).</summary>
public class DuplicateExpressionRuleTests
{
    [Fact]
    public void FlagsRepeatedFunction()
    {
        var findings = RuleTestHelper.Run<DuplicateExpressionRule>(
            "SELECT ABS(x - 10) AS A, CASE WHEN ABS(x - 10) > 5 THEN 1 ELSE 0 END FROM T");

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("ABS");
    }

    [Fact]
    public void FlagsRepeatedCast()
    {
        var findings = RuleTestHelper.Run<DuplicateExpressionRule>(
            "SELECT CAST(Price AS DECIMAL(10,2)), CAST(Price AS DECIMAL(10,2)) * 2 FROM T");

        findings.Should().HaveCount(1);
    }

    [Fact]
    public void DoesNotFlagRepeatedPlainColumns()
    {
        var findings = RuleTestHelper.Run<DuplicateExpressionRule>(
            "SELECT Id, Name FROM T WHERE Id > 1 ORDER BY Id");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagSingleFunctionUsage()
    {
        var findings = RuleTestHelper.Run<DuplicateExpressionRule>(
            "SELECT UPPER(Name) FROM T");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagSameFunctionWithDifferentArguments()
    {
        var findings = RuleTestHelper.Run<DuplicateExpressionRule>(
            "SELECT UPPER(Name), UPPER(City) FROM T");

        findings.Should().BeEmpty();
    }
}
