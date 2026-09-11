using FluentAssertions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Services.Validation;
using Xunit;

namespace SqlOptimizer.Application.Tests.Validation;

/// <summary>
/// Stage C of validation: transformation patterns that may change semantics.
/// These tests pin the conservative behavior: NOT IN/NULL, outer-join
/// predicate movement, correlated subquery-to-JOIN, CAST conversions,
/// OR/IN rewrites and collation-sensitive comparisons must never be marked
/// validated without real evidence.
/// </summary>
public class SqlValidatorSemanticRiskTests
{
    private readonly SqlValidator _validator = ValidationTestSupport.CreateValidator();

    [Fact]
    public async Task NotInToNotExists_IsInconclusive_WithNullBehaviorRisk()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT Id FROM Customers c WHERE c.City NOT IN (SELECT City FROM ExcludedCities)",
            "SELECT Id FROM Customers c WHERE NOT EXISTS (SELECT 1 FROM ExcludedCities x WHERE x.City = c.City)");

        result.Status.Should().Be(ValidationStatus.Inconclusive);
        result.SemanticallyEquivalent.Should().BeFalse();
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.NullBehavior);
    }

    [Fact]
    public async Task NotInToNotExists_NeverValidated_EvenWithSchema()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT Id FROM Customers c WHERE c.City NOT IN (SELECT City FROM ExcludedCities)",
            "SELECT Id FROM Customers c WHERE NOT EXISTS (SELECT 1 FROM ExcludedCities x WHERE x.City = c.City)",
            schema: ValidationTestSupport.TestSchema());

        result.Status.Should().NotBe(ValidationStatus.Passed);
        result.SemanticallyEquivalent.Should().BeFalse();
    }

    [Fact]
    public async Task CastToTryConvert_Fails_WithNullBehavior()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT CAST(Name AS INT) FROM Customers",
            "SELECT TRY_CONVERT(INT, Name) FROM Customers");

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.NullBehavior && r.Provable);
    }

    [Fact]
    public async Task CastToConvert_IsInconclusive_WithImplicitConversionRisk()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT CAST(Name AS NVARCHAR(50)) FROM Customers",
            "SELECT CONVERT(NVARCHAR(50), Name) FROM Customers");

        result.Status.Should().Be(ValidationStatus.Inconclusive);
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.ImplicitConversion);
    }

    [Fact]
    public async Task CorrelatedSubqueryToJoin_IsInconclusive_NeverValidated()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT c.Id, (SELECT MAX(o.Total) FROM Orders o WHERE o.CustomerId = c.Id) AS MaxTotal FROM Customers c",
            "SELECT c.Id, m.MaxTotal FROM Customers c LEFT JOIN (SELECT CustomerId, MAX(Total) AS MaxTotal FROM Orders GROUP BY CustomerId) m ON m.CustomerId = c.Id");

        result.Status.Should().Be(ValidationStatus.Inconclusive);
        result.SemanticallyEquivalent.Should().BeFalse();
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.CorrelatedSubquery);
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.JoinCardinality);
    }

    [Fact]
    public async Task LeftJoinPredicateMovedFromWhereToOn_Fails()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT c.Id FROM Customers c LEFT JOIN Orders o ON o.CustomerId = c.Id WHERE o.Total > 100",
            "SELECT c.Id FROM Customers c LEFT JOIN Orders o ON o.CustomerId = c.Id AND o.Total > 100");

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.OuterJoinSemantics && r.Provable);
    }

    [Fact]
    public async Task OrPredicateToInList_IsInconclusive()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT Id FROM Customers WHERE (Id = 1 AND City = 'Rome') OR (Id = 2 AND City = 'Milan')",
            "SELECT Id FROM Customers WHERE Id IN (1, 2)");

        result.Status.Should().Be(ValidationStatus.Inconclusive);
        result.SemanticallyEquivalent.Should().BeFalse();
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.PredicateLogic);
    }

    [Fact]
    public async Task CountStarToCountNullableColumn_Fails()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT COUNT(*) FROM Customers",
            "SELECT COUNT(Name) FROM Customers",
            schema: ValidationTestSupport.TestSchema());

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.AggregationSemantics && r.Provable);
    }

    [Fact]
    public async Task CountStarToCountNonNullableColumn_Passes()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT COUNT(*) FROM Customers",
            "SELECT COUNT(Id) FROM Customers",
            schema: ValidationTestSupport.TestSchema());

        result.Status.Should().Be(ValidationStatus.Passed);
        result.Evidence.Should().Contain(e => e.Contains("COUNT(*)", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CountStarToCountColumn_WithoutSchema_IsInconclusive()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT COUNT(*) FROM Customers",
            "SELECT COUNT(Name) FROM Customers");

        result.Status.Should().Be(ValidationStatus.Inconclusive);
        result.SemanticallyEquivalent.Should().BeFalse();
    }

    [Fact]
    public async Task SelectStarExpandedWithSchema_Passes()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT * FROM Customers",
            "SELECT Id, Name, City FROM Customers",
            schema: ValidationTestSupport.TestSchema());

        result.Status.Should().Be(ValidationStatus.Passed);
        result.Evidence.Should().Contain(e => e.Contains("star expansion", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SelectStarExpandedWithQualifiedTableAndSchema_Passes()
    {
        // Regression guard: a schema-qualified FROM ("dbo.Customers") must still
        // resolve the single base table for the metadata-backed star-expansion
        // proof. Before the qualified-name lookup fix this returned
        // Inconclusive because FindTable(string) only matches bare table names.
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT * FROM dbo.Customers",
            "SELECT Id, Name, City FROM dbo.Customers",
            schema: ValidationTestSupport.TestSchema());

        result.Status.Should().Be(ValidationStatus.Passed);
        result.Evidence.Should().Contain(e => e.Contains("star expansion", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SelectStarExpandedWithoutSchema_IsInconclusive()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT * FROM Customers",
            "SELECT Id, Name, City FROM Customers");

        result.Status.Should().Be(ValidationStatus.Inconclusive);
        result.SemanticallyEquivalent.Should().BeFalse();
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.Projection);
    }

    [Fact]
    public async Task SelectStarExpandedWrongColumns_Fails()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT * FROM Customers",
            "SELECT Id, Name FROM Customers",
            schema: ValidationTestSupport.TestSchema());

        result.Status.Should().Be(ValidationStatus.Failed);
    }

    [Fact]
    public async Task LikePatternCaseChange_IsInconclusive_WithCollationRisk()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT Id FROM Customers WHERE Name LIKE 'A%'",
            "SELECT Id FROM Customers WHERE Name LIKE 'a%'");

        result.Status.Should().Be(ValidationStatus.Inconclusive);
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.Collation);
    }
}
