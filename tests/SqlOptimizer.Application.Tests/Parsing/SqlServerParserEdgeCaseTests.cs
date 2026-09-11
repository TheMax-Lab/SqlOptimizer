using FluentAssertions;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Infrastructure.Parsing;
using Xunit;

namespace SqlOptimizer.Application.Tests.Parsing;

/// <summary>
/// M12 regression tests for parser edge cases not pinned by the main parser
/// suite: TOP / OFFSET-FETCH degradation warnings, comments, formatting
/// invariance, quoted identifiers, string literals containing SQL keywords,
/// RIGHT/FULL joins, multi-part names, literal type families, nested
/// parentheses and repeated-parse determinism. All tests are offline.
/// </summary>
public sealed class SqlServerParserEdgeCaseTests
{
    private static SqlServerSqlParser Parser { get; } = new();

    // ------------------------------------------------------------------
    // Documented degradation: clauses without an AST representation
    // ------------------------------------------------------------------

    [Fact]
    public void TopClause_ParsesAndReportsWarning()
    {
        var parsed = Parser.Parse("SELECT TOP 10 Id FROM dbo.Customers ORDER BY Id");

        parsed.Warnings.Should().Contain(w => w.Contains("TOP", StringComparison.OrdinalIgnoreCase),
            "TOP is not represented in the analysis model and must be reported, not silently dropped");
        parsed.Root.SelectItems.Should().HaveCount(1);
        parsed.Root.From!.Source.Should().BeOfType<TableReference>()
            .Which.Name.Should().Be("Customers");
    }

    [Fact]
    public void OffsetFetch_ParsesAndReportsWarning()
    {
        var parsed = Parser.Parse(
            "SELECT Id FROM dbo.Customers ORDER BY Id OFFSET 20 ROWS FETCH NEXT 20 ROWS ONLY");

        parsed.Warnings.Should().Contain(w => w.Contains("OFFSET/FETCH", StringComparison.OrdinalIgnoreCase),
            "OFFSET/FETCH is not represented in the analysis model and must be reported, not silently dropped");
        parsed.Root.OrderBy.Should().ContainSingle();
    }

    // ------------------------------------------------------------------
    // Comments and formatting
    // ------------------------------------------------------------------

    [Fact]
    public void LineAndBlockComments_AreIgnored()
    {
        var parsed = Parser.Parse("""
            /* leading block comment */
            SELECT Id, /* inline */ Name
            FROM dbo.Customers -- trailing line comment
            WHERE Id > 10
            """);

        parsed.Warnings.Should().BeEmpty();
        parsed.Root.SelectItems.Should().HaveCount(2);
        parsed.Root.Where.Should().BeOfType<BinaryExpression>()
            .Which.Right.Should().BeOfType<LiteralExpression>();
    }

    [Fact]
    public void Comment_CannotHideASecondStatement()
    {
        // The batch separator is still a separator when a comment follows it:
        // a "SELECT 1; -- note\nSELECT 2" script is two statements.
        var act = () => Parser.Parse("SELECT 1; -- note\nSELECT 2");

        act.Should().Throw<SqlInvalidInputException>();
    }

    [Fact]
    public void WhitespaceAndNewlineDifferences_ProduceEquivalentAst()
    {
        var compact = Parser.Parse("SELECT Id FROM dbo.Customers WHERE Id > 10");
        var expanded = Parser.Parse(
            "SELECT   Id\n\tFROM    dbo.Customers\n\tWHERE   Id\t>\t10");

        expanded.Root.Should().BeEquivalentTo(compact.Root,
            "formatting must not change the parsed structure");
    }

    // ------------------------------------------------------------------
    // Identifiers and literals
    // ------------------------------------------------------------------

    [Fact]
    public void QuotedIdentifiers_Parse()
    {
        var parsed = Parser.Parse("SELECT [Id] FROM [dbo].[Customers] WHERE [Id] = 1");

        var item = parsed.Root.SelectItems.Should().ContainSingle().Subject;
        var column = item.Expression.Should().BeOfType<ColumnExpression>().Subject;
        column.Name.Should().Contain("Id");
        parsed.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void StringLiteralContainingSqlKeywords_IsALiteralNotAStatement()
    {
        var parsed = Parser.Parse(
            "SELECT 'INSERT INTO dbo.T (Id) VALUES (1); DROP TABLE dbo.T' AS Note FROM dbo.T");

        var item = parsed.Root.SelectItems.Should().ContainSingle().Subject;
        var literal = item.Expression.Should().BeOfType<LiteralExpression>().Subject;
        literal.IsString.Should().BeTrue();
        literal.Value.Should().Contain("INSERT INTO");
        parsed.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void NumericAndDateLiterals_PreserveTypeFamily()
    {
        var parsed = Parser.Parse(
            "SELECT * FROM dbo.T WHERE Id = 5 AND Amount = 1.5 AND WhenDue = '2024-01-01' AND Flag = NULL");

        // Left-associative chain: ((Id = 5 AND Amount = 1.5) AND WhenDue = '...') AND Flag = NULL
        var and1 = parsed.Root.Where.Should().BeOfType<BinaryExpression>().Subject;
        var and2 = and1.Left.Should().BeOfType<BinaryExpression>().Subject;
        var and3 = and2.Left.Should().BeOfType<BinaryExpression>().Subject;

        // Operands are typed as SqlExpression, so each comparison node must be
        // narrowed to BinaryExpression before its right-hand literal can be read.
        var eqId = and3.Left.Should().BeOfType<BinaryExpression>().Subject;
        var eqAmount = and3.Right.Should().BeOfType<BinaryExpression>().Subject;
        var eqWhenDue = and2.Right.Should().BeOfType<BinaryExpression>().Subject;
        var eqFlag = and1.Right.Should().BeOfType<BinaryExpression>().Subject;

        eqId.Right.Should().BeOfType<LiteralExpression>()
            .Which.IsNumeric.Should().BeTrue("integer literal");
        eqAmount.Right.Should().BeOfType<LiteralExpression>()
            .Which.IsNumeric.Should().BeTrue("decimal literal");
        eqWhenDue.Right.Should().BeOfType<LiteralExpression>()
            .Which.IsString.Should().BeTrue("string literal");
        eqFlag.Right.Should().BeOfType<LiteralExpression>()
            .Which.IsNull.Should().BeTrue("NULL literal");
    }

    [Fact]
    public void ParenthesizedBooleanPredicate_IsFlattened()
    {
        var parsed = Parser.Parse("SELECT * FROM dbo.T WHERE (A = 1 AND B = 2)");

        var where = parsed.Root.Where.Should().BeOfType<BinaryExpression>().Subject;
        where.Operator.Should().Be(SqlBinaryOperator.And);
        parsed.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void RedundantScalarParentheses_AreRejectedWithParseError()
    {
        // Documented conservative boundary: a scalar ParenthesisExpression has
        // no AST representation, so it is rejected instead of silently dropped.
        var act = () => Parser.Parse("SELECT * FROM dbo.T WHERE A = (1 + 2)");

        act.Should().Throw<SqlParseException>()
            .WithMessage("*ParenthesisExpression*");
    }

    // ------------------------------------------------------------------
    // Joins and multi-part names
    // ------------------------------------------------------------------

    [Fact]
    public void RightJoin_ProducesRightJoinType()
    {
        var parsed = Parser.Parse(
            "SELECT c.Id FROM dbo.Customers c RIGHT JOIN dbo.Orders o ON o.CustomerId = c.Id");

        parsed.Root.From!.Source.Should().BeOfType<JoinSource>()
            .Which.Type.Should().Be(JoinType.Right);
    }

    [Fact]
    public void FullOuterJoin_ProducesFullJoinType()
    {
        var parsed = Parser.Parse(
            "SELECT c.Id FROM dbo.Customers c FULL OUTER JOIN dbo.Orders o ON o.CustomerId = c.Id");

        parsed.Root.From!.Source.Should().BeOfType<JoinSource>()
            .Which.Type.Should().Be(JoinType.Full);
    }

    [Fact]
    public void ThreePartName_IsFlattenedWithWarning()
    {
        var parsed = Parser.Parse("SELECT * FROM [otherdb].[dbo].[Customers]");

        parsed.Warnings.Should().Contain(w => w.Contains("flattened", StringComparison.OrdinalIgnoreCase));
        var table = parsed.Root.From!.Source.Should().BeOfType<TableReference>().Subject;
        table.Schema.Should().Be("dbo");
        table.Name.Should().Be("Customers");
    }

    // ------------------------------------------------------------------
    // Determinism
    // ------------------------------------------------------------------

    [Fact]
    public void Warnings_DonotLeakAcrossParseCallsOnTheSameParserInstance()
    {
        // M12 regression (parser state leak): the same parser instance is
        // reused across Parse calls (as the API scoped registration does for
        // the original SQL and every candidate). Non-fatal warnings are
        // per-query and must never leak from one parse into the next.
        var warningful = Parser.Parse("SELECT TOP 5 Id FROM dbo.Customers");
        warningful.Warnings.Should().Contain(w => w.Contains("TOP", StringComparison.OrdinalIgnoreCase));

        var clean = Parser.Parse("SELECT Id FROM dbo.Customers WHERE Id > 10");

        clean.Warnings.Should().BeEmpty(
            "a clean query parsed on the same parser instance must not inherit warnings from earlier parses");
    }

    [Fact]
    public void ParsingIsDeterministic_RepeatedParsesProduceEquivalentAst()
    {
        const string sql = """
            SELECT c.Id, COUNT(*) AS Cnt
            FROM dbo.Customers c
            INNER JOIN dbo.Orders o ON o.CustomerId = c.Id
            WHERE c.City IN ('Rome', 'Milan')
            GROUP BY c.Id
            HAVING COUNT(*) > 1
            ORDER BY c.Id DESC
            """;

        var first = Parser.Parse(sql);
        var second = Parser.Parse(sql);

        second.Root.Should().BeEquivalentTo(first.Root,
            "the same input must always produce the same AST");
        second.Warnings.Should().Equal(first.Warnings);
    }
}

