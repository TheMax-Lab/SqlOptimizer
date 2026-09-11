using FluentAssertions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Services.Validation;
using Xunit;

namespace SqlOptimizer.Application.Tests.Validation;

/// <summary>
/// Stage B of validation: structural contract checks. Provable changes to
/// the result contract (tables, parameters, projection, DISTINCT, set
/// operations, joins, grouping, ordering) must fail validation; pure
/// formatting and canonicalized predicate commutation must pass; data-
/// dependent changes must be inconclusive, never silently passed.
/// </summary>
public class SqlValidatorStructureTests
{
    private readonly SqlValidator _validator = ValidationTestSupport.CreateValidator();

    [Fact]
    public async Task FormattingAndIdentifierCase_Passes()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "select id from customers where id > 10",
            "SELECT  Id   FROM   Customers   WHERE   Id > 10");

        result.Status.Should().Be(ValidationStatus.Passed);
        result.Evidence.Should().Contain(e => e.Contains("structurally identical", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DifferentTables_Fails()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator, "SELECT Id FROM Customers", "SELECT Id FROM Orders");

        result.Status.Should().Be(ValidationStatus.Failed);
        result.Differences.Should().Contain(d => d.Contains("tables differ", StringComparison.OrdinalIgnoreCase));
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.StatementStructure && r.Provable);
        result.AffectedObjects.Should().Contain("Customers", "Orders");
    }

    [Fact]
    public async Task MissingParameter_Fails()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT Id FROM Customers WHERE Id = @Id",
            "SELECT Id FROM Customers WHERE Id = 5");

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.ParameterUsage && r.Provable);
    }

    [Fact]
    public async Task AddedProjectionColumn_Fails()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator, "SELECT Id FROM Customers", "SELECT Id, Name FROM Customers");

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.Projection && r.Provable);
    }

    [Fact]
    public async Task RemovedProjectionColumn_Fails()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator, "SELECT Id, Name FROM Customers", "SELECT Id FROM Customers");

        result.Status.Should().Be(ValidationStatus.Failed);
    }

    [Fact]
    public async Task ProjectionAliasChange_Fails()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator, "SELECT Id AS CustomerId FROM Customers", "SELECT Id FROM Customers");

        result.Status.Should().Be(ValidationStatus.Failed);
        result.Differences.Should().Contain(d => d.Contains("alias", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DistinctRemoved_Fails()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator, "SELECT DISTINCT Id FROM Customers", "SELECT Id FROM Customers");

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.DuplicateElimination && r.Provable);
    }

    [Fact]
    public async Task UnionToUnionAll_Fails()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT Id FROM Customers UNION SELECT OrderId FROM Orders",
            "SELECT Id FROM Customers UNION ALL SELECT OrderId FROM Orders");

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.DuplicateElimination && r.Provable);
    }

    [Fact]
    public async Task UnionAllToUnion_Fails()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT Id FROM Customers UNION ALL SELECT OrderId FROM Orders",
            "SELECT Id FROM Customers UNION SELECT OrderId FROM Orders");

        result.Status.Should().Be(ValidationStatus.Failed);
    }

    [Fact]
    public async Task OrderByRemoved_Fails()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator, "SELECT Id FROM Customers ORDER BY Id", "SELECT Id FROM Customers");

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.Ordering && r.Provable);
    }

    [Fact]
    public async Task GroupByChanged_Fails()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT City, COUNT(*) FROM Customers GROUP BY City",
            "SELECT Name, COUNT(*) FROM Customers GROUP BY Name");

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.AggregationSemantics && r.Provable);
    }

    [Fact]
    public async Task JoinTypeChanged_Fails()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT c.Id FROM Customers c INNER JOIN Orders o ON o.CustomerId = c.Id",
            "SELECT c.Id FROM Customers c LEFT JOIN Orders o ON o.CustomerId = c.Id");

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.OuterJoinSemantics && r.Provable);
    }

    [Fact]
    public async Task JoinAdded_Fails()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT c.Id FROM Customers c",
            "SELECT c.Id FROM Customers c INNER JOIN Orders o ON o.CustomerId = c.Id");

        result.Status.Should().Be(ValidationStatus.Failed);
    }

    [Fact]
    public async Task AndCommutation_Passes()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator,
            "SELECT Id FROM Customers WHERE Id > 10 AND City = 'Rome'",
            "SELECT Id FROM Customers WHERE City = 'Rome' AND Id > 10");

        result.Status.Should().Be(ValidationStatus.Passed);
        result.SemanticRisks.Should().BeEmpty();
    }

    [Fact]
    public async Task WhereValueChange_IsInconclusive_NotFailed()
    {
        var result = await ValidationTestSupport.ValidateAsync(
            _validator, "SELECT Id FROM Customers WHERE Id = 10", "SELECT Id FROM Customers WHERE Id = 20");

        result.Status.Should().Be(ValidationStatus.Inconclusive);
        result.SemanticallyEquivalent.Should().BeFalse();
        result.SemanticRisks.Should().Contain(r => r.Type == SemanticRiskType.PredicateLogic);
        result.Limitations.Should().Contain(l => l.Contains("static analysis", StringComparison.OrdinalIgnoreCase));
    }
}
