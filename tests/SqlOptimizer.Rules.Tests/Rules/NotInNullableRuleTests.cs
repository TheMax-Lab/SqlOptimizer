using FluentAssertions;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Rules.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Rules.Tests.Rules;

/// <summary>Tests for SQL008 (NotInNullableRule).</summary>
public class NotInNullableRuleTests
{
    [Fact]
    public void FlagsNotInSubqueryWithoutMetadata()
    {
        var findings = RuleTestHelper.Run<NotInNullableRule>(
            "SELECT Id FROM Customers WHERE Id NOT IN (SELECT CustomerId FROM Orders)");

        findings.Should().HaveCount(1);
        findings[0].Severity.Should().Be(Severity.High);
        findings[0].Confidence.Should().BeLessThan(0.8);
    }

    [Fact]
    public void FlagsNullableSubqueryColumnWithHighConfidence()
    {
        var schema = new SqlOptimizer.Domain.Metadata.DatabaseSchema(
        [
            new SqlOptimizer.Domain.Metadata.DatabaseTable("dbo", "Customers", null,
                [new SqlOptimizer.Domain.Metadata.DatabaseColumn("Id", "INT", false, true)], []),
            new SqlOptimizer.Domain.Metadata.DatabaseTable("dbo", "Orders", null,
                [new SqlOptimizer.Domain.Metadata.DatabaseColumn("CustomerId", "INT", true, false)], [])
        ]);

        var findings = RuleTestHelper.Run<NotInNullableRule>(
            "SELECT Id FROM Customers WHERE Id NOT IN (SELECT CustomerId FROM Orders)",
            schema);

        findings.Should().HaveCount(1);
        findings[0].Severity.Should().Be(Severity.Critical);
        findings[0].Confidence.Should().BeGreaterOrEqualTo(0.9);
    }

    [Fact]
    public void DoesNotFlagWhenSubqueryColumnIsProvenNotNull()
    {
        var schema = new SqlOptimizer.Domain.Metadata.DatabaseSchema(
        [
            new SqlOptimizer.Domain.Metadata.DatabaseTable("dbo", "Customers", null,
                [new SqlOptimizer.Domain.Metadata.DatabaseColumn("Id", "INT", false, true)], []),
            new SqlOptimizer.Domain.Metadata.DatabaseTable("dbo", "Orders", null,
                [new SqlOptimizer.Domain.Metadata.DatabaseColumn("CustomerId", "INT", false, true)], [])
        ]);

        var findings = RuleTestHelper.Run<NotInNullableRule>(
            "SELECT Id FROM Customers WHERE Id NOT IN (SELECT CustomerId FROM Orders)",
            schema);

        findings.Should().BeEmpty();
    }

    [Fact]
    public void FlagsNullLiteralInNotInList()
    {
        var findings = RuleTestHelper.Run<NotInNullableRule>(
            "SELECT Id FROM T WHERE Id NOT IN (1, NULL)");

        findings.Should().HaveCount(1);
        findings[0].Severity.Should().Be(Severity.Critical);
        findings[0].Confidence.Should().Be(1.0);
    }

    [Fact]
    public void DoesNotFlagPlainIn()
    {
        var findings = RuleTestHelper.Run<NotInNullableRule>(
            "SELECT Id FROM T WHERE Id IN (1, 2, 3)");

        findings.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotFlagNotInWithoutNulls()
    {
        var findings = RuleTestHelper.Run<NotInNullableRule>(
            "SELECT Id FROM T WHERE Id NOT IN (1, 2)");

        findings.Should().BeEmpty();
    }
}
