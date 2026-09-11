using FluentAssertions;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Tests.Ast;
using Xunit;

namespace SqlOptimizer.Domain.Tests.Ast.Visitors;

/// <summary>
/// Tests for the deterministic AST traversal infrastructure
/// (<see cref="SqlAstWalker"/> and <see cref="SqlAstVisitor"/>).
/// </summary>
public class SqlAstWalkerTests
{
    [Fact]
    public void AllNodes_IncludesRootAndAllDescendants()
    {
        var root = AstFactory.SimpleSelect();

        var nodes = SqlAstWalker.AllNodes(root).ToList();

        nodes[0].Should().BeSameAs(root);
        nodes.Should().Contain(new FromClause(root.From!.Source));
        nodes.OfType<ColumnExpression>().Should().HaveCount(2);
    }

    [Fact]
    public void AllNodes_IsPreOrder_DepthFirst()
    {
        var root = AstFactory.WithScalarSubquery();

        var nodes = SqlAstWalker.AllNodes(root).ToList();
        var subqueryIndex = nodes.ToList().IndexOf(nodes.First(n => n is SubqueryExpression));
        var innerIndex = nodes.ToList().IndexOf(nodes.First(n => n is SelectStatement s && !ReferenceEquals(s, root)));

        innerIndex.Should().BeGreaterThan(subqueryIndex);
    }

    [Fact]
    public void OfType_ReturnsOnlyMatchingNodes()
    {
        var root = AstFactory.JoinSelect();

        var joins = SqlAstWalker.OfType<JoinSource>(root).ToList();
        var tables = SqlAstWalker.OfType<TableReference>(root).ToList();

        joins.Should().HaveCount(1);
        tables.Should().HaveCount(2);
    }

    [Fact]
    public void Find_ReturnsFirstMatch_And_FindReturnsAllMatches()
    {
        var root = AstFactory.JoinSelect();

        var first = SqlAstWalker.FindFirst(root, n => n is TableReference);
        var all = SqlAstWalker.Find(root, n => n is TableReference).ToList();

        first.Should().BeOfType<TableReference>();
        all.Should().HaveCount(2);
    }

    [Fact]
    public void Count_CountsMatchingNodes()
    {
        var root = AstFactory.SimpleSelect();

        SqlAstWalker.Count(root, n => n is ColumnExpression).Should().Be(2);
        SqlAstWalker.Count(root, n => n is TableReference).Should().Be(1);
    }

    [Fact]
    public void DirectNodes_DoesNotCrossSubqueryBoundaries()
    {
        var root = AstFactory.WithScalarSubquery();

        var direct = SqlAstWalker.DirectNodes(root).ToList();

        direct.Should().Contain(root);
        direct.Should().Contain(n => n is SubqueryExpression);
        direct.Should().NotContain(n => n is AggregateExpression);
        direct.OfType<SelectStatement>().Should().OnlyContain(s => ReferenceEquals(s, root));
    }
}

/// <summary>
/// Tests for the default visitor dispatch of <see cref="SqlAstVisitor"/>.
/// </summary>
public class SqlAstVisitorTests
{
    private sealed class CountingVisitor : SqlAstVisitor
    {
        public int Visited;

        public override void VisitSelectStatement(SelectStatement node)
        {
            Visited++;
            base.VisitSelectStatement(node);
        }

        public override void VisitColumnExpression(ColumnExpression node)
        {
            Visited++;
            base.VisitColumnExpression(node);
        }
    }

    [Fact]
    public void Visit_DispatchesToNodeSpecificMethods_AndContinuesTraversal()
    {
        var root = AstFactory.SimpleSelect();
        var visitor = new CountingVisitor();

        visitor.Visit(root);

        visitor.Visited.Should().Be(3);
    }

    [Fact]
    public void Visit_HandlesNullNode()
    {
        var visitor = new CountingVisitor();

        var act = () => visitor.Visit(null);

        act.Should().NotThrow();
    }
}
