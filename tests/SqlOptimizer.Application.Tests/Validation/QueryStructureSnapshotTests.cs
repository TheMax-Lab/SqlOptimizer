using FluentAssertions;
using SqlOptimizer.Application.Services.Validation;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Infrastructure.Parsing;
using Xunit;

namespace SqlOptimizer.Application.Tests.Validation;

/// <summary>
/// Unit tests of the structural snapshot: determinism, canonical predicate
/// normalization (AND/OR commutation and flattening) and structural capture
/// (tables, joins, subqueries, set operations).
/// </summary>
public class QueryStructureSnapshotTests
{
    private readonly SqlServerSqlParser _parser = new();

    private QueryStructureSnapshot Build(string sql) => QueryStructureSnapshot.Build(_parser.Parse(sql).Root);

    [Fact]
    public void Fingerprint_IsStable_AcrossCaseAndWhitespace()
    {
        var a = Build("select id from customers where id > 10");
        var b = Build("SELECT   Id  FROM  Customers  WHERE  Id > 10");

        a.Fingerprint.Should().Be(b.Fingerprint);
    }

    [Fact]
    public void Fingerprint_Changes_When_StructureChanges()
    {
        var a = Build("SELECT Id FROM Customers WHERE Id = 10");
        var b = Build("SELECT Id FROM Customers WHERE Id = 20");

        a.Fingerprint.Should().NotBe(b.Fingerprint);
    }

    [Fact]
    public void CanonicalPredicate_AndCommutation_IsEqual()
    {
        var a = _parser.Parse("SELECT Id FROM Customers WHERE Id > 10 AND City = 'Rome'").Root;
        var b = _parser.Parse("SELECT Id FROM Customers WHERE City = 'Rome' AND Id > 10").Root;

        QueryStructureSnapshot.CanonicalPredicate(a.Where!).Should()
            .Be(QueryStructureSnapshot.CanonicalPredicate(b.Where!));
    }

    [Fact]
    public void CanonicalPredicate_OrFlattening_SortsOperands()
    {
        var a = _parser.Parse("SELECT Id FROM Customers WHERE Id = 1 OR Name = 'a' OR City = 'Rome'").Root;
        var b = _parser.Parse("SELECT Id FROM Customers WHERE City = 'Rome' OR Id = 1 OR Name = 'a'").Root;

        QueryStructureSnapshot.CanonicalPredicate(a.Where!).Should()
            .Be(QueryStructureSnapshot.CanonicalPredicate(b.Where!));
    }

    [Fact]
    public void Snapshot_Captures_TablesJoinsAndSubqueries()
    {
        var snapshot = Build(
            "SELECT c.Id FROM Customers c INNER JOIN Orders o ON o.CustomerId = c.Id WHERE c.Id IN (SELECT CustomerId FROM Orders)");

        snapshot.Tables.Should().BeEquivalentTo("Customers", "Orders");
        snapshot.JoinCount.Should().Be(1);
        snapshot.JoinTypes.Should().BeEquivalentTo("Inner");
        snapshot.SubqueryCount.Should().Be(1);
    }

    [Fact]
    public void Snapshot_CapturesSetOperatorsAndParameters()
    {
        var snapshot = Build(
            "SELECT Id FROM Customers WHERE Id = @Id UNION ALL SELECT OrderId FROM Orders WHERE OrderId = @Oid");

        snapshot.SetOperators.Should().BeEquivalentTo("UNION ALL");
        snapshot.Parameters.Should().BeEquivalentTo("@Id", "@Oid");
    }

    [Fact]
    public void Snapshot_DetectsCorrelatedSubquery()
    {
        var snapshot = Build(
            "SELECT c.Id FROM Customers c WHERE c.Id IN (SELECT CustomerId FROM Orders o WHERE o.CustomerId = c.Id)");

        snapshot.HasCorrelatedSubquery.Should().BeTrue();
    }
}
