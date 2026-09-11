using SqlOptimizer.Domain.AST;

namespace SqlOptimizer.Domain.Tests.Ast;

/// <summary>
/// Builds small hand-crafted ASTs so the traversal infrastructure can be
/// tested deterministically without a parser.
/// </summary>
public static class AstFactory
{
    /// <summary>A simple single-table SELECT with one WHERE comparison.</summary>
    public static SelectStatement SimpleSelect() => new(
        SelectItems: [new SelectItem(new ColumnExpression("Id"), null)],
        From: new FromClause(new TableReference("", "Customers", null)),
        Where: new BinaryExpression(
            SqlBinaryOperator.Equal,
            new ColumnExpression("Id"),
            new LiteralExpression("1", "int")),
        GroupBy: [],
        Having: null,
        OrderBy: [],
        Distinct: false);

    /// <summary>A two-table inner join with an ON predicate.</summary>
    public static SelectStatement JoinSelect()
    {
        var left = new TableReference("", "Customers", "c");
        var right = new TableReference("", "Orders", "o");
        var on = new BinaryExpression(
            SqlBinaryOperator.Equal,
            new ColumnExpression("CustomerId", "o"),
            new ColumnExpression("Id", "c"));

        return new SelectStatement(
            [new SelectItem(new ColumnExpression("Name", "c"), null)],
            new FromClause(new JoinSource(left, right, JoinType.Inner, on)),
            null,
            [],
            null,
            [],
            false);
    }

    /// <summary>A SELECT with a scalar subquery in the SELECT list.</summary>
    public static SelectStatement WithScalarSubquery()
    {
        var inner = new SelectStatement(
            [new SelectItem(new AggregateExpression("COUNT", []), null)],
            new FromClause(new TableReference("", "Orders", null)),
            null,
            [],
            null,
            [],
            false);

        return new SelectStatement(
            [new SelectItem(new SubqueryExpression(inner), null)],
            new FromClause(new TableReference("", "Customers", null)),
            null,
            [],
            null,
            [],
            false);
    }

    /// <summary>A SELECT with a correlated EXISTS subquery.</summary>
    public static SelectStatement WithCorrelatedExists()
    {
        var inner = new SelectStatement(
            [new SelectItem(new LiteralExpression("1", "int"), null)],
            new FromClause(new TableReference("", "Orders", "o")),
            new BinaryExpression(
                SqlBinaryOperator.Equal,
                new ColumnExpression("CustomerId", "o"),
                new ColumnExpression("Id", "c")),
            [],
            null,
            [],
            false);

        return new SelectStatement(
            [new SelectItem(new ColumnExpression("Id", "c"), null)],
            new FromClause(new TableReference("", "Customers", "c")),
            new ExistsExpression(new SubqueryExpression(inner)),
            [],
            null,
            [],
            false);
    }

    /// <summary>A SELECT with a CROSS JOIN.</summary>
    public static SelectStatement CrossJoinSelect() => new(
        [new SelectItem(new ColumnExpression("X", "a"), null)],
        new FromClause(new JoinSource(
            new TableReference("", "A", "a"),
            new TableReference("", "B", "b"),
            JoinType.Cross,
            null)),
        null,
        [],
        null,
        [],
        false);
}
