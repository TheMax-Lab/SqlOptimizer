using FluentAssertions;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Tests.Ast;
using Xunit;

namespace SqlOptimizer.Domain.Tests.Ast.Visitors;

/// <summary>Tests for <see cref="JoinFinder"/>.</summary>
public class JoinFinderTests
{
    [Fact]
    public void FindJoins_DiscoversJoinsOfAStatement()
    {
        JoinFinder.FindJoins(AstFactory.JoinSelect()).Should().HaveCount(1);
        JoinFinder.FindJoins(AstFactory.SimpleSelect()).Should().BeEmpty();
    }

    [Fact]
    public void FindCrossJoins_AndFindOuterJoins_FilterByType()
    {
        JoinFinder.FindCrossJoins(AstFactory.CrossJoinSelect()).Should().HaveCount(1);
        JoinFinder.FindOuterJoins(AstFactory.CrossJoinSelect()).Should().BeEmpty();
        JoinFinder.FindOuterJoins(AstFactory.JoinSelect()).Should().BeEmpty();
    }

    [Fact]
    public void FindJoins_HandlesNullSource()
    {
        JoinFinder.FindJoins((FromSource?)null).Should().BeEmpty();
    }
}

/// <summary>Tests for <see cref="SubqueryFinder"/>.</summary>
public class SubqueryFinderTests
{
    [Fact]
    public void CountSubqueries_CountsScalarAndDerivedSubqueries()
    {
        SubqueryFinder.CountSubqueries(AstFactory.WithScalarSubquery()).Should().Be(1);
        SubqueryFinder.CountSubqueries(AstFactory.SimpleSelect()).Should().Be(0);
    }

    [Fact]
    public void GetMaxDepth_MeasuresNesting()
    {
        SubqueryFinder.GetMaxDepth(AstFactory.WithScalarSubquery()).Should().Be(1);

        // Explicit depth 2: T1 -> subquery(T2) -> subquery(T3).
        var level3 = new SelectStatement(
            [new SelectItem(new AggregateExpression("COUNT", []), null)],
            new FromClause(new TableReference("", "T3", null)),
            null, [], null, [], false);

        var level2 = new SelectStatement(
            [new SelectItem(new SubqueryExpression(level3), null)],
            new FromClause(new TableReference("", "T2", null)),
            null, [], null, [], false);

        var level1 = new SelectStatement(
            [new SelectItem(new SubqueryExpression(level2), null)],
            new FromClause(new TableReference("", "T1", null)),
            null, [], null, [], false);

        SubqueryFinder.GetMaxDepth(level1).Should().Be(2);
    }

    [Fact]
    public void IsCorrelated_DetectsOuterScopeReferences()
    {
        var root = AstFactory.WithCorrelatedExists();
        var correlated = ((ExistsExpression)root.Where!).Subquery;

        SubqueryFinder.IsCorrelated(root, correlated).Should().BeTrue();

        var uncorrelated = new SubqueryExpression(new SelectStatement(
            [new SelectItem(new LiteralExpression("1", "int"), null)],
            new FromClause(new TableReference("", "Orders", "o")),
            new BinaryExpression(
                SqlBinaryOperator.Equal,
                new ColumnExpression("CustomerId", "o"),
                new LiteralExpression("1", "int")),
            [], null, [], false));

        SubqueryFinder.IsCorrelated(root, uncorrelated).Should().BeFalse();
    }

    [Fact]
    public void FindDirectSubqueries_OnlyReturnsDirectSubqueries()
    {
        var root = AstFactory.WithCorrelatedExists();

        SubqueryFinder.FindDirectSubqueries(root).Should().HaveCount(1);
    }

    [Fact]
    public void GetDirectScopeAliases_OnlyIncludesOwnFromAndCtes()
    {
        var aliases = SubqueryFinder.GetDirectScopeAliases(AstFactory.WithCorrelatedExists());

        aliases.Should().BeEquivalentTo(["c"]);
    }
}

/// <summary>Tests for <see cref="SqlExpressionFinder"/>.</summary>
public class SqlExpressionFinderTests
{
    [Fact]
    public void FindPredicateExpressions_IncludesWhereAndJoinOn()
    {
        var root = AstFactory.JoinSelect();

        SqlExpressionFinder.FindPredicateExpressions(root).Should().HaveCount(1);
        SqlExpressionFinder.FindPredicateExpressions(root).Should().OnlyContain(p => p is BinaryExpression);
    }

    [Fact]
    public void FindAtomicPredicates_SplitsAndOrLeaves()
    {
        var where = new BinaryExpression(
            SqlBinaryOperator.And,
            new BinaryExpression(SqlBinaryOperator.Equal, new ColumnExpression("A"), new LiteralExpression("1", "int")),
            new BinaryExpression(
                SqlBinaryOperator.Or,
                new BinaryExpression(SqlBinaryOperator.Equal, new ColumnExpression("B"), new LiteralExpression("2", "int")),
                new BinaryExpression(SqlBinaryOperator.Equal, new ColumnExpression("C"), new LiteralExpression("3", "int"))));

        var root = new SelectStatement(
            [new SelectItem(new ColumnExpression("A"), null)],
            new FromClause(new TableReference("", "T", null)),
            where, [], null, [], false);

        SqlExpressionFinder.FindAtomicPredicates(root).Should().HaveCount(3);
    }

    [Fact]
    public void FindComparisons_ExcludesBooleanOperators()
    {
        var root = AstFactory.SimpleSelect();

        SqlExpressionFinder.FindComparisons(root).Should().HaveCount(1);
        SqlExpressionFinder.FindBooleanExpressions(root).Should().BeEmpty();
    }

    [Fact]
    public void SplitPredicates_ReturnsLeavesOfBooleanTree()
    {
        var expression = new BinaryExpression(
            SqlBinaryOperator.And,
            new BinaryExpression(SqlBinaryOperator.Equal, new ColumnExpression("A"), new LiteralExpression("1", "int")),
            new BinaryExpression(SqlBinaryOperator.Equal, new ColumnExpression("B"), new LiteralExpression("2", "int")));

        SqlExpressionFinder.SplitPredicates(expression).Should().HaveCount(2);
        SqlExpressionFinder.SplitPredicates(new ColumnExpression("A")).Should().HaveCount(1);
    }
}
