using FluentAssertions;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL016 (UnnecessaryCastRule).</summary>
public class UnnecessaryCastRuleTests
{
    [Fact]
    public void FlagsNestedCastToSameType()
    {
        var findings = RuleTestHelper.Run<UnnecessaryCastRule>(
            "SELECT CAST(CAST(Id AS INT) AS INT) FROM T");

        findings.Should().HaveCount(1);
        findings[0].Severity.Should().Be(Severity.Info);
    }

    [Fact]
    public void FlagsCastToOwnTypeWithMetadata()
    {
        var findings = RuleTestHelper.Run<UnnecessaryCastRule>(
            "SELECT CAST(Id AS INT) FROM T",
            RuleTestHelper.BuildSchema(("Id", "INT", false, true)));

        findings.Should().HaveCount(1);
    }

    [Fact]
    public void DoesNotFlagCastToDifferentType()
    {
        var findings = RuleTestHelper.Run<UnnecessaryCastRule>(
            "SELECT CAST(Price AS DECIMAL(10,2)) FROM T");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagCastToDifferentLength()
    {
        var findings = RuleTestHelper.Run<UnnecessaryCastRule>(
            "SELECT CAST(Name AS NVARCHAR(50)) FROM T",
            RuleTestHelper.BuildSchema(("Name", "NVARCHAR(100)", true, false)));

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagColumnCastWithoutMetadata()
    {
        var findings = RuleTestHelper.Run<UnnecessaryCastRule>(
            "SELECT CAST(Id AS INT) FROM T");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void FindingIsMaintainabilityNotPerformance()
    {
        var finding = RuleTestHelper.Run<UnnecessaryCastRule>(
            "SELECT CAST(CAST(Id AS INT) AS INT) FROM T")[0];

        finding.Category.Should().Be(FindingCategory.Maintainability);
        finding.Impact.Performance.Should().Be(0);
    }
}
