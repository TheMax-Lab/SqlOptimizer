using FluentAssertions;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Parsing;
using Xunit;

namespace SqlOptimizer.Domain.Tests.Ast;

/// <summary>
/// M12 contract tests for the AST value model: record equality/value
/// semantics that the parser and validator tests rely on, literal type
/// family flags, parse-result defaults and enum boundaries. The AST is
/// hand-crafted so the Domain layer stays parser independent.
/// </summary>
public class AstContractTests
{
    [Fact]
    public void ColumnExpression_ImplementsValueEquality()
    {
        var a = new ColumnExpression("Id", "c");
        var b = new ColumnExpression("Id", "c");
        var c = new ColumnExpression("Id");

        a.Should().Be(b);
        a.Should().NotBe(c);
        a.IsQualified.Should().BeTrue();
        c.IsQualified.Should().BeFalse();
    }

    [Fact]
    public void LiteralExpression_TypeFamilyFlags()
    {
        new LiteralExpression("1", "int").IsNumeric.Should().BeTrue();
        new LiteralExpression("1.5", "decimal").IsNumeric.Should().BeTrue();
        new LiteralExpression("'x'", "nvarchar").IsString.Should().BeTrue();
        new LiteralExpression("NULL", "null").IsNull.Should().BeTrue();
        new LiteralExpression("'2024-01-01'", "date").IsDateTime.Should().BeTrue();
        new LiteralExpression("1", "int").IsString.Should().BeFalse();
        new LiteralExpression("'x'", "nvarchar").IsNumeric.Should().BeFalse();
    }

    [Fact]
    public void SelectStatement_ComparisonsAreStructuralOnContent()
    {
        // Leaf records implement true value equality (this is what the parser
        // tests rely on); statement-level structures compare structurally by
        // content (collection-typed properties do not use reference equality
        // for structural comparison).
        var item = new SelectItem(new ColumnExpression("Id"), null);
        var from = new FromClause(new TableReference("", "T", null));
        var a = new SelectStatement([item], from, null, [], null, [], false);
        var b = new SelectStatement(
            [new SelectItem(new ColumnExpression("Id"), null)],
            new FromClause(new TableReference("", "T", null)),
            null, [], null, [], false);
        var withDistinct = new SelectStatement([item], from, null, [], null, [], true);

        a.Should().BeEquivalentTo(b, "structurally identical statements compare equal by content");
        a.Should().NotBeEquivalentTo(withDistinct);
    }

    [Fact]
    public void ParsedQuery_DefaultsWarningsToEmptyList()
    {
        var root = new SelectStatement([], null, null, [], null, [], false);
        var parsed = new ParsedQuery(root);

        parsed.Warnings.Should().NotBeNull().And.BeEmpty();
        parsed.Root.Should().BeSameAs(root);
    }

    [Fact]
    public void JoinType_ContainsExactlyTheFiveSupportedJoins()
    {
        Enum.GetValues<JoinType>().Should().BeEquivalentTo(
            [JoinType.Inner, JoinType.Left, JoinType.Right, JoinType.Full, JoinType.Cross]);
    }

    [Fact]
    public void SqlDialect_ReservedDialectsAreDeclared()
    {
        // The boundary: only SqlServer is implemented; the others exist as
        // reserved values so request validation can name them explicitly.
        Enum.GetValues<SqlDialect>().Should().BeEquivalentTo(
            [SqlDialect.SqlServer, SqlDialect.PostgreSql, SqlDialect.MySql, SqlDialect.Oracle]);
    }
}
