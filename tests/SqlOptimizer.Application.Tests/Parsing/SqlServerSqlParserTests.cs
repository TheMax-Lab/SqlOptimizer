using FluentAssertions;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Parsing;
using SqlOptimizer.Infrastructure.Parsing;
using Xunit;

namespace SqlOptimizer.Application.Tests.Parsing;

/// <summary>
/// Tests for the ScriptDom based <see cref="SqlServerSqlParser"/> and the
/// ScriptDom-to-AST conversion. Each test parses a single SELECT statement
/// and asserts on the resulting parser independent AST.
/// </summary>
public sealed class SqlServerSqlParserTests
{
    private static SelectStatement ParseSql(string sql) =>
        new SqlServerSqlParser().Parse(sql).Root;

    [Fact]
    public void Dialect_IsSqlServer()
    {
        new SqlServerSqlParser().Dialect.Should().Be(SqlDialect.SqlServer);
    }

    // ------------------------------------------------------------------
    // Basic statement structure
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_SimpleSelect_ProducesSelectStatement()
    {
        var root = ParseSql("SELECT Id, Name FROM dbo.Customers WHERE Status = 1");

        root.SelectItems.Should().HaveCount(2);
        root.SelectItems[0].Expression.Should().Be(new ColumnExpression("Id"));
        root.SelectItems[1].Expression.Should().Be(new ColumnExpression("Name"));

        root.From.Should().NotBeNull();
        var table = root.From!.Source
            .Should().BeOfType<TableReference>().Subject;
        table.Schema.Should().Be("dbo");
        table.Name.Should().Be("Customers");
        table.Alias.Should().BeNull();

        var where = root.Where.Should().BeOfType<BinaryExpression>().Subject;
        where.Operator.Should().Be(SqlBinaryOperator.Equal);
        where.Left.Should().Be(new ColumnExpression("Status"));
        where.Right.Should().BeOfType<LiteralExpression>()
            .Which.Value.Should().Be("1");
        where.Right.Should().BeOfType<LiteralExpression>()
            .Which.IsNumeric.Should().BeTrue();
    }

    [Fact]
    public void Parse_StarSelect_ProducesUnqualifiedStarItem()
    {
        var root = ParseSql("SELECT * FROM dbo.Customers");

        var item = root.SelectItems.Should().ContainSingle().Subject;
        item.IsStar.Should().BeTrue();
        item.StarTableAlias.Should().BeNull();
        item.Expression.Should().BeNull();
    }

    [Fact]
    public void Parse_QualifiedStarSelect_ProducesStarWithTableAlias()
    {
        var root = ParseSql("SELECT c.* FROM dbo.Customers AS c");

        var item = root.SelectItems.Should().ContainSingle().Subject;
        item.IsStar.Should().BeTrue();
        item.StarTableAlias.Should().Be("c");
    }

    [Fact]
    public void Parse_SelectItems_KeepAliases()
    {
        var root = ParseSql(
            "SELECT c.Id AS CustomerId, c.Name AS CustomerName FROM dbo.Customers AS c");

        root.SelectItems[0].Alias.Should().Be("CustomerId");
        root.SelectItems[1].Alias.Should().Be("CustomerName");

        var table = root.From!.Source.Should().BeOfType<TableReference>().Subject;
        table.Alias.Should().Be("c");
        table.Qualifier.Should().Be("c");
    }

    [Fact]
    public void Parse_Distinct_SetsDistinctFlag()
    {
        var root = ParseSql("SELECT DISTINCT Name FROM dbo.Customers");

        root.Distinct.Should().BeTrue();
    }

    [Fact]
    public void Parse_GroupByAndHaving_ProducesGroupsAndPredicate()
    {
        var root = ParseSql(
            """
            SELECT c.Id, COUNT(*) AS Cnt
            FROM dbo.Orders AS o
            INNER JOIN dbo.Customers AS c ON c.Id = o.CustomerId
            GROUP BY c.Id
            HAVING COUNT(*) > 5
            """);

        root.GroupBy.Should().ContainSingle()
            .Which.Should().Be(new ColumnExpression("Id", "c"));

        var having = root.Having.Should().BeOfType<BinaryExpression>().Subject;
        having.Operator.Should().Be(SqlBinaryOperator.GreaterThan);
        var countAggregate = having.Left.Should().BeOfType<AggregateExpression>().Subject;
        Assert.Equal("COUNT", countAggregate.FunctionName, ignoreCase: true);
        countAggregate.CountStar.Should().BeTrue();
    }

    [Fact]
    public void Parse_OrderBy_PreservesItemsAndDirection()
    {
        var root = ParseSql(
            "SELECT Id, Name FROM dbo.Customers ORDER BY Name DESC, Id");

        root.OrderBy.Should().HaveCount(2);
        root.OrderBy[0].Expression.Should().Be(new ColumnExpression("Name"));
        root.OrderBy[0].Ascending.Should().BeFalse();
        root.OrderBy[1].Expression.Should().Be(new ColumnExpression("Id"));
        root.OrderBy[1].Ascending.Should().BeTrue();
    }

    [Fact]
    public void Parse_DerivedTable_ProducesSubquerySource()
    {
        var root = ParseSql(
            "SELECT x.Id FROM (SELECT Id FROM dbo.Things) AS x");

        var source = root.From!.Source.Should().BeOfType<SubquerySource>().Subject;
        source.Alias.Should().Be("x");
        source.Query.From!.Source.Should().BeOfType<TableReference>()
            .Which.Name.Should().Be("Things");
    }

    [Fact]
    public void Parse_ScalarSubquery_ProducesSubqueryExpression()
    {
        var root = ParseSql(
            "SELECT (SELECT MAX(Total) FROM dbo.Invoices) AS MaxTotal FROM dbo.Things");

        var expression = root.SelectItems.Should().ContainSingle().Subject.Expression
            .Should().BeOfType<SubqueryExpression>().Subject;
        expression.Alias.Should().BeNull();
        var inner = expression.Statement.SelectItems.Should().ContainSingle().Subject
            .Expression.Should().BeOfType<AggregateExpression>().Subject;
        inner.FunctionName.Should().NotBeNullOrEmpty();
        Assert.Equal("MAX", inner.FunctionName, ignoreCase: true);
        inner.Arguments.Should().ContainSingle()
            .Which.Should().Be(new ColumnExpression("Total"));
    }

    // ------------------------------------------------------------------
    // Joins and CTEs
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_InnerJoin_ProducesJoinSource()
    {
        var root = ParseSql(
            """
            SELECT o.OrderId
            FROM dbo.Orders AS o
            INNER JOIN dbo.OrderDetails AS d ON d.OrderId = o.OrderId
            """);

        var join = root.From!.Source.Should().BeOfType<JoinSource>().Subject;
        join.Type.Should().Be(JoinType.Inner);
        join.Left.Should().Be(new TableReference("dbo", "Orders", "o"));
        join.Right.Should().Be(new TableReference("dbo", "OrderDetails", "d"));

        var predicate = join.Predicate.Should().BeOfType<BinaryExpression>().Subject;
        predicate.Operator.Should().Be(SqlBinaryOperator.Equal);
        predicate.Left.Should().Be(new ColumnExpression("OrderId", "d"));
        predicate.Right.Should().Be(new ColumnExpression("OrderId", "o"));
        join.HasMeaningfulPredicate.Should().BeTrue();
    }

    [Fact]
    public void Parse_LeftJoin_ProducesJoinSourceWithLeftType()
    {
        var root = ParseSql(
            """
            SELECT o.Id
            FROM dbo.Orders AS o
            LEFT JOIN dbo.Shipments AS s ON s.OrderId = o.Id
            """);

        var join = root.From!.Source.Should().BeOfType<JoinSource>().Subject;
        join.Type.Should().Be(JoinType.Left);
    }

    [Fact]
    public void Parse_Cte_ProducesCommonTableExpression()
    {
        var root = ParseSql(
            """
            WITH ActiveCustomers (Id, Name) AS (
                SELECT Id, Name FROM dbo.Customers WHERE Status = 1
            )
            SELECT Id FROM ActiveCustomers
            """);

        var cte = root.Ctes.Should().ContainSingle().Subject;
        cte.Name.Should().Be("ActiveCustomers");
        cte.Columns.Should().ContainInOrder("Id", "Name");
        cte.IsRecursive.Should().BeFalse();

        var cteSelect = cte.Query.SelectItems[0].Expression
            .Should().BeOfType<ColumnExpression>().Subject;
        cteSelect.Name.Should().Be("Id");

        root.From!.Source.Should().BeOfType<TableReference>()
            .Which.Name.Should().Be("ActiveCustomers");
    }

    [Fact]
    public void Parse_SetOperation_UnionAll_ChainsRightStatement()
    {
        var root = ParseSql(
            """
            SELECT Id FROM dbo.Customers
            UNION ALL
            SELECT Id FROM dbo.ArchivedCustomers
            """);

        root.SetOperation.Should().NotBeNull();
        var setOperation = root.SetOperation!;
        setOperation.Operator.Should().Be(SetOperatorKind.UnionAll);
        setOperation.Right.From!.Source.Should().BeOfType<TableReference>()
            .Which.Name.Should().Be("ArchivedCustomers");
    }

    [Theory]
    [InlineData("UNION", SetOperatorKind.Union)]
    [InlineData("INTERSECT", SetOperatorKind.Intersect)]
    [InlineData("EXCEPT", SetOperatorKind.Except)]
    public void Parse_SetOperations_MapOperator(string keyword, SetOperatorKind expected)
    {
        var root = ParseSql(
            $"""
            SELECT Id FROM dbo.Customers
            {keyword}
            SELECT Id FROM dbo.ArchivedCustomers
            """);

        root.SetOperation.Should().NotBeNull();
        root.SetOperation!.Operator.Should().Be(expected);
    }

    // ------------------------------------------------------------------
    // Predicates
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_InValueList_ProducesInExpression()
    {
        var root = ParseSql(
            "SELECT * FROM dbo.Things WHERE Kind IN (1, 2, 3)");

        var predicate = root.Where.Should().BeOfType<InExpression>().Subject;
        predicate.Expression.Should().Be(new ColumnExpression("Kind"));
        predicate.HasValueList.Should().BeTrue();
        predicate.HasSubquery.Should().BeFalse();
        predicate.Not.Should().BeFalse();
        predicate.Values.Should().HaveCount(3);
        predicate.Values.OfType<LiteralExpression>().Should().HaveCount(3);
    }

    [Fact]
    public void Parse_NotIn_SetsNotFlag()
    {
        var root = ParseSql(
            "SELECT * FROM dbo.Things WHERE Kind NOT IN (1, 2)");

        var predicate = root.Where.Should().BeOfType<InExpression>().Subject;
        predicate.Not.Should().BeTrue();
    }

    [Fact]
    public void Parse_InSubquery_ProducesInExpressionWithSubquery()
    {
        var root = ParseSql(
            """
            SELECT o.Id
            FROM dbo.Orders AS o
            WHERE o.CustomerId IN (SELECT Id FROM dbo.Customers WHERE Status = 1)
            """);

        var predicate = root.Where.Should().BeOfType<InExpression>().Subject;
        predicate.HasSubquery.Should().BeTrue();
        predicate.HasValueList.Should().BeFalse();
        predicate.Subquery!.Statement.From!.Source.Should().BeOfType<TableReference>()
            .Which.Name.Should().Be("Customers");
    }

    [Fact]
    public void Parse_Exists_ProducesExistsExpression()
    {
        var root = ParseSql(
            """
            SELECT c.Id
            FROM dbo.Customers AS c
            WHERE EXISTS (SELECT 1 FROM dbo.Orders AS o WHERE o.CustomerId = c.Id)
            """);

        var predicate = root.Where.Should().BeOfType<ExistsExpression>().Subject;
        predicate.Not.Should().BeFalse();
        predicate.Subquery.Statement.From!.Source.Should().BeOfType<TableReference>()
            .Which.Name.Should().Be("Orders");
    }

    [Fact]
    public void Parse_NotExists_SetsNotFlag()
    {
        var root = ParseSql(
            """
            SELECT c.Id
            FROM dbo.Customers AS c
            WHERE NOT EXISTS (SELECT 1 FROM dbo.Orders AS o WHERE o.CustomerId = c.Id)
            """);

        root.Where.Should().BeOfType<ExistsExpression>()
            .Which.Not.Should().BeTrue();
    }

    [Fact]
    public void Parse_Like_ProducesLikeExpression()
    {
        var root = ParseSql(
            "SELECT * FROM dbo.Customers WHERE Name LIKE 'A%'");

        var predicate = root.Where.Should().BeOfType<LikeExpression>().Subject;
        predicate.Expression.Should().Be(new ColumnExpression("Name"));
        predicate.Not.Should().BeFalse();
        predicate.Pattern.Should().BeOfType<LiteralExpression>()
            .Which.IsString.Should().BeTrue();
    }

    [Fact]
    public void Parse_NotLike_SetsNotFlag()
    {
        var root = ParseSql(
            "SELECT * FROM dbo.Customers WHERE Name NOT LIKE 'A%'");

        root.Where.Should().BeOfType<LikeExpression>()
            .Which.Not.Should().BeTrue();
    }

    [Fact]
    public void Parse_Between_RewritesToComparisonPair()
    {
        var root = ParseSql(
            """
            SELECT * FROM dbo.Orders
            WHERE OrderDate BETWEEN '2024-01-01' AND '2024-01-31'
            """);

        var and = root.Where.Should().BeOfType<BinaryExpression>().Subject;
        and.Operator.Should().Be(SqlBinaryOperator.And);

        var lower = and.Left.Should().BeOfType<BinaryExpression>().Subject;
        var upper = and.Right.Should().BeOfType<BinaryExpression>().Subject;

        new[] { lower.Operator, upper.Operator }.Should().BeEquivalentTo(new[]
        {
            SqlBinaryOperator.GreaterThanOrEqual,
            SqlBinaryOperator.LessThanOrEqual
        });
        lower.Left.Should().Be(new ColumnExpression("OrderDate"));
        upper.Left.Should().Be(new ColumnExpression("OrderDate"));
    }

    [Fact]
    public void Parse_IsNull_ProducesIsComparison()
    {
        var root = ParseSql("SELECT * FROM dbo.Customers WHERE Notes IS NULL");

        root.Where.Should().BeOfType<BinaryExpression>()
            .Which.Operator.Should().Be(SqlBinaryOperator.Is);
    }

    [Fact]
    public void Parse_IsNotNull_ProducesIsNotComparison()
    {
        var root = ParseSql("SELECT * FROM dbo.Customers WHERE Notes IS NOT NULL");

        root.Where.Should().BeOfType<BinaryExpression>()
            .Which.Operator.Should().Be(SqlBinaryOperator.IsNot);
    }

    [Fact]
    public void Parse_AndPredicate_ProducesBinaryAnd()
    {
        var root = ParseSql(
            "SELECT * FROM dbo.Things WHERE A = 1 AND B = 2");

        var and = root.Where.Should().BeOfType<BinaryExpression>().Subject;
        and.Operator.Should().Be(SqlBinaryOperator.And);
        and.Left.Should().BeOfType<BinaryExpression>()
            .Which.IsComparison.Should().BeTrue();
        and.Right.Should().BeOfType<BinaryExpression>()
            .Which.IsComparison.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Expressions
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_Parameter_ProducesParameterExpression()
    {
        var root = ParseSql("SELECT * FROM dbo.Things WHERE Id = @p1");

        root.Where!.Should().BeOfType<BinaryExpression>()
            .Which.Right.Should().Be(new ParameterExpression("@p1"));
    }

    [Fact]
    public void Parse_Literals_PreserveTypeFamily()
    {
        var root = ParseSql("SELECT 42, N'abc', NULL");

        var integer = root.SelectItems[0].Expression
            .Should().BeOfType<LiteralExpression>().Subject;
        integer.Value.Should().Be("42");
        integer.IsNumeric.Should().BeTrue();

        var text = root.SelectItems[1].Expression
            .Should().BeOfType<LiteralExpression>().Subject;
        text.IsString.Should().BeTrue();

        var nullLiteral = root.SelectItems[2].Expression
            .Should().BeOfType<LiteralExpression>().Subject;
        nullLiteral.IsNull.Should().BeTrue();
    }

    [Fact]
    public void Parse_Arithmetic_ProducesBinaryExpression()
    {
        var root = ParseSql("SELECT Price * Quantity AS Total FROM dbo.Things");

        var expression = root.SelectItems.Should().ContainSingle().Subject.Expression
            .Should().BeOfType<BinaryExpression>().Subject;
        expression.Operator.Should().Be(SqlBinaryOperator.Multiply);
        expression.IsArithmetic.Should().BeTrue();
        expression.Left.Should().Be(new ColumnExpression("Price"));
        expression.Right.Should().Be(new ColumnExpression("Quantity"));
    }

    [Fact]
    public void Parse_Not_ProducesUnaryExpression()
    {
        var root = ParseSql("SELECT * FROM dbo.Things WHERE NOT (Kind = 1)");

        root.Where.Should().BeOfType<UnaryExpression>()
            .Which.Operator.Should().Be(SqlUnaryOperator.Not);
        root.Where.Should().BeOfType<UnaryExpression>()
            .Which.Operand.Should().BeOfType<BinaryExpression>()
                .Which.IsComparison.Should().BeTrue();
    }

    [Fact]
    public void Parse_Case_ProducesCaseExpression()
    {
        var root = ParseSql(
            """
            SELECT CASE WHEN Price > 100 THEN 'high' ELSE 'low' END AS Band
            FROM dbo.Things
            """);

        var expression = root.SelectItems.Should().ContainSingle().Subject.Expression
            .Should().BeOfType<CaseExpression>().Subject;
        expression.Operand.Should().BeNull();
        expression.Whens.Should().ContainSingle();
        expression.Whens[0].When.Should().BeOfType<BinaryExpression>()
            .Which.Operator.Should().Be(SqlBinaryOperator.GreaterThan);
        expression.Whens[0].Then.Should().BeOfType<LiteralExpression>()
            .Which.IsString.Should().BeTrue();
        expression.Else.Should().BeOfType<LiteralExpression>()
            .Which.IsString.Should().BeTrue();
    }

    [Fact]
    public void Parse_Cast_ProducesCastExpression()
    {
        var root = ParseSql("SELECT CAST(OrderDate AS DATE) FROM dbo.Orders");

        var expression = root.SelectItems.Should().ContainSingle().Subject.Expression
            .Should().BeOfType<CastExpression>().Subject;
        expression.Kind.Should().Be(CastKind.Cast);
        Assert.Equal("DATE", expression.TargetType, ignoreCase: true);
        expression.Expression.Should().Be(new ColumnExpression("OrderDate"));
    }

    [Fact]
    public void Parse_Convert_ProducesCastExpressionWithConvertKind()
    {
        var root = ParseSql("SELECT CONVERT(NVARCHAR(100), OrderDate) FROM dbo.Orders");

        var expression = root.SelectItems.Should().ContainSingle().Subject.Expression
            .Should().BeOfType<CastExpression>().Subject;
        expression.Kind.Should().Be(CastKind.Convert);
        expression.Expression.Should().Be(new ColumnExpression("OrderDate"));
    }

    [Fact]
    public void Parse_TryConvert_ProducesCastExpressionWithTryConvertKind()
    {
        var root = ParseSql("SELECT TRY_CONVERT(DATE, OrderDate) FROM dbo.Orders");

        var expression = root.SelectItems.Should().ContainSingle().Subject.Expression
            .Should().BeOfType<CastExpression>().Subject;
        expression.Kind.Should().Be(CastKind.TryConvert);
        expression.Expression.Should().Be(new ColumnExpression("OrderDate"));
    }

    [Fact]
    public void Parse_CountStar_ProducesAggregateWithCountStar()
    {
        var root = ParseSql("SELECT COUNT(*) FROM dbo.Things");

        var aggregate = root.SelectItems.Should().ContainSingle().Subject.Expression
            .Should().BeOfType<AggregateExpression>().Subject;
        Assert.Equal("COUNT", aggregate.FunctionName, ignoreCase: true);
        aggregate.CountStar.Should().BeTrue();
        aggregate.Distinct.Should().BeFalse();
        aggregate.Arguments.Should().BeEmpty();
    }

    [Fact]
    public void Parse_CountColumn_ProducesAggregateWithColumnArgument()
    {
        var root = ParseSql("SELECT COUNT(t.Id) FROM dbo.Things AS t");

        var aggregate = root.SelectItems.Should().ContainSingle().Subject.Expression
            .Should().BeOfType<AggregateExpression>().Subject;
        aggregate.CountStar.Should().BeFalse();
        aggregate.Arguments.Should().ContainSingle()
            .Which.Should().Be(new ColumnExpression("Id", "t"));
    }

    [Fact]
    public void Parse_CountDistinct_ProducesAggregateWithDistinct()
    {
        var root = ParseSql("SELECT COUNT(DISTINCT t.Kind) FROM dbo.Things AS t");

        var aggregate = root.SelectItems.Should().ContainSingle().Subject.Expression
            .Should().BeOfType<AggregateExpression>().Subject;
        aggregate.Distinct.Should().BeTrue();
        aggregate.CountStar.Should().BeFalse();
    }

    [Fact]
    public void Parse_WindowFunction_ProducesWindowClause()
    {
        var root = ParseSql(
            """
            SELECT ROW_NUMBER() OVER (PARTITION BY c.Id ORDER BY o.OrderDate DESC) AS RowNum
            FROM dbo.Orders AS o
            INNER JOIN dbo.Customers AS c ON c.Id = o.CustomerId
            """);

        var expression = root.SelectItems.Should().ContainSingle().Subject.Expression
            .Should().BeOfType<FunctionExpression>().Subject;
        Assert.Equal("ROW_NUMBER", expression.Name, ignoreCase: true);
        expression.IsWindowed.Should().BeTrue();
        expression.Window!.PartitionBy.Should().ContainSingle()
            .Which.Should().Be(new ColumnExpression("Id", "c"));
        expression.Window.OrderBy.Should().ContainSingle()
            .Which.Expression.Should().Be(new ColumnExpression("OrderDate", "o"));
        expression.Window.OrderBy[0].Ascending.Should().BeFalse();
        expression.Window.FrameText.Should().BeNull();
    }

    [Fact]
    public void Parse_WindowedAggregate_ProducesAggregateWithWindow()
    {
        var root = ParseSql(
            "SELECT SUM(o.Amount) OVER (PARTITION BY o.CustomerId) FROM dbo.Orders AS o");

        var aggregate = root.SelectItems.Should().ContainSingle().Subject.Expression
            .Should().BeOfType<AggregateExpression>().Subject;
        aggregate.IsWindowed.Should().BeTrue();
        aggregate.CountStar.Should().BeFalse();
        aggregate.Window!.PartitionBy.Should().ContainSingle()
            .Which.Should().Be(new ColumnExpression("CustomerId", "o"));
    }

    // ------------------------------------------------------------------
    // Rejections
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t\r\n")]
    public void Parse_EmptySql_ThrowsSqlInvalidInputException(string sql)
    {
        var act = () => new SqlServerSqlParser().Parse(sql);

        act.Should().Throw<SqlInvalidInputException>();
    }

    [Fact]
    public void Parse_MultipleStatements_ThrowsSqlInvalidInputException()
    {
        var act = () => new SqlServerSqlParser().Parse("SELECT 1; SELECT 2");

        act.Should().Throw<SqlInvalidInputException>();
    }

    [Theory]
    [InlineData("UPDATE dbo.Things SET A = 1")]
    [InlineData("DELETE FROM dbo.Things")]
    [InlineData("INSERT INTO dbo.Things (A) VALUES (1)")]
    public void Parse_DmlStatements_ThrowSqlInvalidInputException(string sql)
    {
        var act = () => new SqlServerSqlParser().Parse(sql);

        act.Should().Throw<SqlInvalidInputException>();
    }

    [Theory]
    [InlineData("CREATE TABLE dbo.Things (Id INT)")]
    [InlineData("DROP TABLE dbo.Things")]
    [InlineData("ALTER TABLE dbo.Things ADD A INT")]
    [InlineData("EXEC sp_help")]
    public void Parse_NonSelectStatements_ThrowSqlInvalidInputException(string sql)
    {
        var act = () => new SqlServerSqlParser().Parse(sql);

        act.Should().Throw<SqlInvalidInputException>();
    }

    [Theory]
    [InlineData("SELECT * FROM")]
    [InlineData("SELECT 1 +")]
    [InlineData("SELECT (1")]
    [InlineData("SELECT * FROM dbo.Things WHERE")]
    public void Parse_InvalidSql_ThrowsSqlParseException(string sql)
    {
        var act = () => new SqlServerSqlParser().Parse(sql);

        act.Should().Throw<SqlParseException>();
    }
}
