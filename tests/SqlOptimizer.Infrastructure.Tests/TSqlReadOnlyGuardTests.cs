using FluentAssertions;
using SqlOptimizer.Infrastructure.Parsing;
using SqlOptimizer.Infrastructure.Validation;
using Xunit;

namespace SqlOptimizer.Infrastructure.Tests;

/// <summary>
/// Fail-closed read-only gate: only a single provably read-only SELECT is
/// accepted. DML, DDL, EXEC, transactions, multi-statement batches, SELECT
/// INTO, linked-server/cross-database references and table variables are
/// all rejected before anything could be executed.
/// </summary>
public class TSqlReadOnlyGuardTests
{
    private static readonly SqlServerSqlParser Parser = new();

    [Theory]
    [InlineData("SELECT Id FROM Customers")]
    [InlineData("SELECT c.Id FROM Customers c JOIN Orders o ON o.CustomerId = c.Id WHERE o.Total > 0")]
    [InlineData("SELECT COUNT(*) FROM Customers GROUP BY City HAVING COUNT(*) > 1")]
    [InlineData("WITH cte AS (SELECT 1 AS N) SELECT N FROM cte")]
    [InlineData("SELECT Id FROM Customers UNION ALL SELECT OrderId FROM Orders")]
    [InlineData("SELECT Id FROM Customers ORDER BY Id DESC")]
    public void PlainReadOnlySelects_AreAccepted(string sql)
    {
        var outcome = TSqlReadOnlyGuard.Check(sql, Parser);

        outcome.IsReadOnly.Should().BeTrue();
        outcome.Reason.Should().BeNull();
        outcome.Statement.Should().NotBeNull();
    }

    [Fact]
    public void BuiltInTableValuedFunction_IsAccepted_ReadOnly()
    {
        // Built-in TVFs are read-only table functions; the parser models them
        // as table references and they are allowed to run.
        var outcome = TSqlReadOnlyGuard.Check("SELECT value FROM STRING_SPLIT('a,b', ',')", Parser);

        outcome.IsReadOnly.Should().BeTrue();
    }

    [Theory]
    [InlineData("INSERT INTO Customers (Name) VALUES ('x')")]
    [InlineData("UPDATE Customers SET Name = 'x'")]
    [InlineData("DELETE FROM Customers")]
    [InlineData("MERGE INTO Customers c USING Orders o ON c.Id = o.Id WHEN MATCHED THEN UPDATE SET Name = o.Id")]
    [InlineData("CREATE TABLE T (Id INT)")]
    [InlineData("ALTER TABLE Customers ADD X INT")]
    [InlineData("DROP TABLE Customers")]
    [InlineData("TRUNCATE TABLE Customers")]
    [InlineData("EXEC xp_cmdshell 'dir'")]
    [InlineData("BEGIN TRAN; SELECT 1; ROLLBACK")]
    [InlineData("IF 1 = 1 SELECT 1")]
    public void StateChangingOrControlFlowStatements_AreRejected(string sql)
    {
        var outcome = TSqlReadOnlyGuard.Check(sql, Parser);

        outcome.IsReadOnly.Should().BeFalse();
        outcome.Reason.Should().NotBeNullOrWhiteSpace();
        outcome.Statement.Should().BeNull();
    }

    [Fact]
    public void MultiStatementBatch_IsRejected()
    {
        var outcome = TSqlReadOnlyGuard.Check("SELECT 1; SELECT 2", Parser);

        outcome.IsReadOnly.Should().BeFalse();
        outcome.Reason.Should().Contain("single");
    }

    [Fact]
    public void SelectInto_IsRejected()
    {
        var outcome = TSqlReadOnlyGuard.Check("SELECT Id INTO #T FROM Customers", Parser);

        outcome.IsReadOnly.Should().BeFalse();
        outcome.Reason.Should().Contain("SELECT INTO");
    }

    [Fact]
    public void OpenRowset_IsRejected()
    {
        var outcome = TSqlReadOnlyGuard.Check(
            "SELECT * FROM OPENROWSET('SQLOLEDB', 'server';'user';'pass', 'SELECT 1')", Parser);

        outcome.IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public void UserDefinedTableValuedFunction_IsRejected()
    {
        var outcome = TSqlReadOnlyGuard.Check("SELECT * FROM dbo.fn_Customers(1)", Parser);

        outcome.IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public void LinkedServerReference_IsRejected()
    {
        var outcome = TSqlReadOnlyGuard.Check("SELECT * FROM [other-server].[mydb].[dbo].[Customers]", Parser);

        outcome.IsReadOnly.Should().BeFalse();
        outcome.Reason.Should().Contain("read-only");
    }

    [Fact]
    public void CrossDatabaseReference_IsRejected()
    {
        var outcome = TSqlReadOnlyGuard.Check("SELECT * FROM [otherdb].[dbo].[Customers]", Parser);

        outcome.IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public void TableVariable_IsRejected()
    {
        var outcome = TSqlReadOnlyGuard.Check("SELECT * FROM @Customers", Parser);

        outcome.IsReadOnly.Should().BeFalse();
        outcome.Reason.Should().Contain("Table variables");
    }

    [Theory]
    [InlineData("SELECT FROM WHERE")]
    [InlineData("")]
    [InlineData("   ")]
    public void UnparseableOrEmptySql_IsRejected(string sql)
    {
        var outcome = TSqlReadOnlyGuard.Check(sql, Parser);

        outcome.IsReadOnly.Should().BeFalse();
        outcome.Reason.Should().NotBeNullOrWhiteSpace();
    }

    // ------------------------------------------------------------------
    // M12: bypass mutations must not defeat the fail-closed guard.
    // Casing, whitespace and comment mutations are applied to the same
    // state-changing statements already covered above; every variant must
    // still be rejected.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("insert into t (id) values (1)")]
    [InlineData("DeLeTe From T")]
    [InlineData("uPdaTe T set id = 1")]
    [InlineData("cReAtE Table T (id int)")]
    [InlineData("TrUnCaTe TaBlE T")]
    [InlineData("exec xp_cmdshell 'dir'")]
    [InlineData("EXECUTE xp_cmdshell 'dir'")]
    public void MutatedStateChangingStatements_AreRejected(string sql)
    {
        var outcome = TSqlReadOnlyGuard.Check(sql, Parser);

        outcome.IsReadOnly.Should().BeFalse(
            "casing and keyword-variant mutations must not defeat the read-only guard");
        outcome.Reason.Should().NotBeNullOrWhiteSpace();
        outcome.Statement.Should().BeNull();
    }

    [Theory]
    [InlineData("INSERT\nINTO t (id)\nVALUES (1)")]
    [InlineData("INSERT\tINTO\tt (id) VALUES (1)")]
    [InlineData("   DELETE     FROM      T   ")]
    [InlineData("SELECT 1;-- hidden separator\nSELECT 2")]
    public void WhitespaceMutatedStatements_AreRejected(string sql)
    {
        var outcome = TSqlReadOnlyGuard.Check(sql, Parser);

        outcome.IsReadOnly.Should().BeFalse(
            "whitespace and comment mutations must not defeat the read-only guard");
        outcome.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("INSERT /*c*/ INTO t (id) VALUES (1)")]
    [InlineData("DELETE /*c*/ FROM T")]
    [InlineData("/* leading */ DROP TABLE T /* trailing */")]
    public void CommentMutatedStatements_AreRejected(string sql)
    {
        var outcome = TSqlReadOnlyGuard.Check(sql, Parser);

        outcome.IsReadOnly.Should().BeFalse(
            "comments between keywords must not turn a DML statement into a SELECT");
        outcome.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("SELECT Id /* c */ FROM dbo.Customers -- tail")]
    [InlineData("/* leading block */\nSELECT Id FROM dbo.Customers")]
    public void CommentedReadOnlySelects_RemainAccepted(string sql)
    {
        var outcome = TSqlReadOnlyGuard.Check(sql, Parser);

        outcome.IsReadOnly.Should().BeTrue(
            "comments do not change the statement kind; a safe SELECT stays safe");
        outcome.Statement.Should().NotBeNull();
    }
}



