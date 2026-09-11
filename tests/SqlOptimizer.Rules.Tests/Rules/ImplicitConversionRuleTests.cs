using FluentAssertions;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL005 (ImplicitConversionRule).</summary>
public class ImplicitConversionRuleTests
{
    [Fact]
    public void CanAnalyze_IsFalseWithoutSchema()
    {
        var rule = new ImplicitConversionRule();
        var context = RuleTestHelper.BuildContext("SELECT Id FROM T WHERE Id = 1");

        rule.CanAnalyze(context).Should().BeFalse();
    }

    [Fact]
    public void FlagsVarcharComparedToIntLiteral()
    {
        var findings = RuleTestHelper.Run<ImplicitConversionRule>(
            "SELECT Id FROM T WHERE Code = 42",
            RuleTestHelper.BuildSchema(("Code", "NVARCHAR(20)", true, false)));

        findings.Should().HaveCount(1);
        findings[0].Message.Should().Contain("Code");
        findings[0].Confidence.Should().BeGreaterOrEqualTo(0.8);
    }

    [Fact]
    public void FlagsDatetimeComparedToNumericLiteral()
    {
        var findings = RuleTestHelper.Run<ImplicitConversionRule>(
            "SELECT Id FROM T WHERE OrderDate = 20250101",
            RuleTestHelper.BuildSchema(("OrderDate", "DATETIME", false, false)));

        findings.Should().HaveCount(1);
    }

    [Fact]
    public void FlagsIntComparedToDecimalLiteral()
    {
        var findings = RuleTestHelper.Run<ImplicitConversionRule>(
            "SELECT Id FROM T WHERE Qty = 1.5",
            RuleTestHelper.BuildSchema(("Qty", "INT", false, false)));

        findings.Should().HaveCount(1);
        findings[0].Confidence.Should().BeLessThan(0.7);
    }

    [Fact]
    public void DoesNotFlagMatchingTypes()
    {
        var findings = RuleTestHelper.Run<ImplicitConversionRule>(
            "SELECT Id FROM T WHERE Id = 42",
            RuleTestHelper.BuildSchema(("Id", "INT", false, true)));

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagParameters()
    {
        var findings = RuleTestHelper.Run<ImplicitConversionRule>(
            "SELECT Id FROM T WHERE Code = @code",
            RuleTestHelper.BuildSchema(("Code", "NVARCHAR(20)", true, false)));

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagUnknownColumns()
    {
        var findings = RuleTestHelper.Run<ImplicitConversionRule>(
            "SELECT Id FROM T WHERE Missing = 42",
            RuleTestHelper.BuildSchema(("Id", "INT", false, true)));

        findings.Should().BeEmpty();
    }
}
