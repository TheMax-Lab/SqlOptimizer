using FluentAssertions;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Tests.Ast;
using Xunit;

namespace SqlOptimizer.Domain.Tests.Ast.Visitors;

/// <summary>Tests for <see cref="ColumnReferenceFinder"/>.</summary>
public class ColumnReferenceFinderTests
{
    [Fact]
    public void FindColumns_ReturnsAllColumnReferences()
    {
        var root = AstFactory.SimpleSelect();

        ColumnReferenceFinder.FindColumns(root).Should().HaveCount(2);
    }

    [Fact]
    public void FindColumns_ByTableAlias_FiltersCaseInsensitive()
    {
        var root = AstFactory.JoinSelect();

        ColumnReferenceFinder.FindColumns(root, "c").Should().HaveCount(2);
        ColumnReferenceFinder.FindColumns(root, "C").Should().HaveCount(2);
        ColumnReferenceFinder.FindColumns(root, "o").Should().HaveCount(1);
    }

    [Fact]
    public void FindColumnsByName_MatchesQualifiedAndUnqualified()
    {
        var root = AstFactory.JoinSelect();

        ColumnReferenceFinder.FindColumnsByName(root, "Id").Should().HaveCount(1);
        ColumnReferenceFinder.FindColumnsByName(root, "name").Should().HaveCount(1);
    }

    [Fact]
    public void FindUnqualifiedColumns_ExcludesQualified()
    {
        var root = AstFactory.SimpleSelect();

        ColumnReferenceFinder.FindUnqualifiedColumns(root).Should().HaveCount(2);
        ColumnReferenceFinder.FindUnqualifiedColumns(AstFactory.JoinSelect()).Should().BeEmpty();
    }
}

/// <summary>Tests for <see cref="FunctionFinder"/>.</summary>
public class FunctionFinderTests
{
    [Fact]
    public void FindFunctions_AndFindAggregates_DiscoverUsages()
    {
        var statement = new SelectStatement(
            [
                new SelectItem(new FunctionExpression("UPPER", [new ColumnExpression("Name")]), null),
                new SelectItem(new AggregateExpression("COUNT", []), null)
            ],
            new FromClause(new TableReference("", "T", null)),
            null, [], null, [], false);

        FunctionFinder.FindFunctions(statement).Should().HaveCount(1);
        FunctionFinder.FindFunctions(statement).Should().OnlyContain(f => f.Name == "UPPER");
        FunctionFinder.FindFunctions(statement, "upper").Should().HaveCount(1);
        FunctionFinder.FindFunctions(statement, "lower").Should().BeEmpty();
        FunctionFinder.FindAggregates(statement).Should().HaveCount(1);
    }

    [Fact]
    public void FindCasts_DiscoverCastExpressions()
    {
        var cast = new CastExpression("INT", "int", new ColumnExpression("Id"));
        var statement = new SelectStatement(
            [new SelectItem(cast, null)],
            new FromClause(new TableReference("", "T", null)),
            null, [], null, [], false);

        FunctionFinder.FindCasts(statement).Should().Contain(cast);
    }

    [Fact]
    public void UsesFunction_DetectsNestedUsage()
    {
        var inner = new FunctionExpression("TRIM", [new ColumnExpression("Name")]);
        var expression = new BinaryExpression(
            SqlBinaryOperator.Equal, inner, new LiteralExpression("x", "varchar"));

        FunctionFinder.UsesFunction(expression).Should().BeTrue();
        FunctionFinder.UsesFunction(new ColumnExpression("Name")).Should().BeFalse();
    }
}

/// <summary>Tests for <see cref="TableReferenceFinder"/>.</summary>
public class TableReferenceFinderTests
{
    [Fact]
    public void FindTables_IncludesNestedTables()
    {
        TableReferenceFinder.FindTables(AstFactory.JoinSelect()).Should().HaveCount(2);
        TableReferenceFinder.FindTables(AstFactory.WithScalarSubquery()).Should().HaveCount(2);
    }

    [Fact]
    public void GetTableAliases_MapsAliasOrName()
    {
        var aliases = TableReferenceFinder.GetTableAliases(AstFactory.JoinSelect());
        aliases.Keys.Should().BeEquivalentTo(["c", "o"]);

        var simpleAliases = TableReferenceFinder.GetTableAliases(AstFactory.SimpleSelect());
        simpleAliases.Keys.Should().BeEquivalentTo(["Customers"]);
    }
}
