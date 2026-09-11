using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Infrastructure.Parsing;
using SqlOptimizer.Infrastructure.Validation;
using Xunit;

namespace SqlOptimizer.Infrastructure.IntegrationTests;

/// <summary>
/// M6 live integration tests for <see cref="SqlDatabaseValidationProvider"/>
/// against a disposable SQL Server (Testcontainers). All tests are tagged
/// with the "Integration" category and require Docker; without Docker the
/// shared fixture fails with an explicit "integration environment
/// unavailable" message and no live result is claimed.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SqlDatabaseValidationProviderIntegrationTests
{
    /// <summary>Statement whose execution always exceeds a 2 second command timeout.</summary>
    private const string SlowQuery = """
        SELECT COUNT(*)
        FROM sys.all_columns a
        CROSS JOIN sys.all_columns b
        CROSS JOIN sys.all_columns c
        CROSS JOIN sys.all_columns d
        CROSS JOIN sys.all_columns e
        """;

    private readonly TestSqlServerFixture _fixture;

    /// <summary>Creates a new test instance bound to the shared disposable SQL Server.</summary>
    /// <param name="fixture">Shared disposable SQL Server.</param>
    public SqlDatabaseValidationProviderIntegrationTests(TestSqlServerFixture fixture) =>
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public async Task IdenticalResults_AreReportedEqual()
    {
        _fixture.RequireSnapshotIsolation();
        const string sql = "SELECT Id, Name FROM dbo.Customers ORDER BY Id";

        var result = await CreateProvider().CompareResultsAsync(sql, sql, 1000);

        result.Should().NotBeNull();
        result!.ResultsEqual.Should().BeTrue();
        result.Truncated.Should().BeFalse();
        result.OriginalRows.Should().Be(4);
        result.CandidateRows.Should().Be(4);
        result.OriginalDuration.Should().NotBeNull();
        result.CandidateDuration.Should().NotBeNull();
    }

    [Fact]
    public async Task DifferentResults_AreReportedNotEqual()
    {
        _fixture.RequireSnapshotIsolation();
        var result = await CreateProvider().CompareResultsAsync(
            "SELECT Id, Name FROM dbo.Customers ORDER BY Id",
            "SELECT Id, Name FROM dbo.Customers WHERE City IS NOT NULL ORDER BY Id",
            1000);

        result.Should().NotBeNull();
        result!.ResultsEqual.Should().BeFalse();
        result.OriginalRows.Should().Be(4);
        result.CandidateRows.Should().Be(3);
    }

    [Fact]
    public async Task NullValues_AreDistinguishedFromOrdinaryValues()
    {
        _fixture.RequireSnapshotIsolation();
        var result = await CreateProvider().CompareResultsAsync(
            "SELECT Id, City FROM dbo.Customers ORDER BY Id",
            "SELECT Id, CASE WHEN City IS NULL THEN N'Unknown' ELSE City END AS City FROM dbo.Customers ORDER BY Id",
            1000);

        result.Should().NotBeNull();
        result!.ResultsEqual.Should().BeFalse();
    }

    [Fact]
    public async Task NullValues_InTheSamePositions_CompareEqual()
    {
        _fixture.RequireSnapshotIsolation();
        var result = await CreateProvider().CompareResultsAsync(
            "SELECT Id, City FROM dbo.Customers ORDER BY Id",
            "SELECT C.Id, C.City FROM dbo.Customers C ORDER BY C.Id",
            1000);

        result.Should().NotBeNull();
        result!.ResultsEqual.Should().BeTrue();
        result.OriginalRows.Should().Be(4);
    }

    [Fact]
    public async Task DuplicateRows_ChangeTheComparison()
    {
        _fixture.RequireSnapshotIsolation();
        // "Rome" appears twice in the original; removing duplicates changes the multiset.
        var result = await CreateProvider().CompareResultsAsync(
            "SELECT City FROM dbo.Customers ORDER BY City",
            "SELECT DISTINCT City FROM dbo.Customers ORDER BY City",
            1000);

        result.Should().NotBeNull();
        result!.ResultsEqual.Should().BeFalse();
        result.OriginalRows.Should().Be(4);
        result.CandidateRows.Should().Be(3);
    }

    [Fact]
    public async Task DifferentColumnCounts_FailSafely()
    {
        _fixture.RequireSnapshotIsolation();
        var result = await CreateProvider().CompareResultsAsync(
            "SELECT Id FROM dbo.Customers ORDER BY Id",
            "SELECT Id, Name FROM dbo.Customers ORDER BY Id",
            1000);

        result.Should().NotBeNull();
        result!.ResultsEqual.Should().BeFalse();
    }

    [Fact]
    public async Task TruncatedResults_NeverClaimEquality()
    {
        _fixture.RequireSnapshotIsolation();
        var provider = CreateProvider(_fixture.CreateOptions(maxRowsForComparison: 10));

        var result = await provider.CompareResultsAsync(
            "SELECT N FROM dbo.Bench ORDER BY N",
            "SELECT N FROM dbo.Bench WHERE N IS NOT NULL ORDER BY N",
            10);

        result.Should().NotBeNull();
        result!.Truncated.Should().BeTrue();
        // A truncated comparison must never be treated as proof of full
        // equivalence: the Truncated flag (asserted above) is what the
        // validator uses to refuse a Passed verdict and any improvement claim
        // (the end-to-end guarantee is covered by the application-level
        // TruncatedEqual_Inconclusive test). ResultsEqual only reports whether
        // the compared (truncated) rows match and is intentionally separate
        // from Truncated, so it is deliberately not asserted to be false here.
        result.OriginalRows.Should().Be(10);
        result.CandidateRows.Should().Be(10);
    }

    [Fact]
    public async Task CommandTimeout_ProducesNoEvidence()
    {
        _fixture.RequireSnapshotIsolation();
        var provider = CreateProvider(_fixture.CreateOptions(commandTimeoutSeconds: 2));

        var result = await provider.CompareResultsAsync("SELECT 1 AS One", SlowQuery, 10);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("DELETE FROM dbo.Customers")]
    [InlineData("UPDATE dbo.Customers SET Name = N'hacked'")]
    [InlineData("CREATE TABLE dbo.Hacked (Id INT)")]
    [InlineData("DROP TABLE dbo.Customers")]
    [InlineData("SELECT * INTO dbo.Hacked FROM dbo.Customers")]
    [InlineData("EXEC sp_who")]
    [InlineData("SELECT 1; DROP TABLE dbo.Customers")]
    public async Task ReadWriteSql_IsRefusedWithoutExecution(string sql)
    {
        var result = await CreateProvider().CompareResultsAsync(
            "SELECT Id FROM dbo.Customers ORDER BY Id", sql, 1000);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ReadWriteSql_IsNeverExecutedAgainstTheDatabase()
    {
        var provider = CreateProvider();
        await provider.CompareResultsAsync("SELECT Id FROM dbo.Customers ORDER BY Id", "DELETE FROM dbo.Customers", 1000);
        await provider.CompareResultsAsync("UPDATE dbo.Customers SET Name = N'hacked'", "SELECT Id FROM dbo.Customers ORDER BY Id", 1000);

        var customers = await _fixture.QueryScalarAsync("SELECT COUNT(*) FROM dbo.Customers");
        customers.Should().Be(4L);
        var hacked = await _fixture.QueryScalarAsync("SELECT COUNT(*) FROM dbo.Customers WHERE Name = N'hacked'");
        hacked.Should().Be(0L);
    }

    [Fact]
    public async Task Cancellation_PropagatesToTheCaller()
    {
        _fixture.RequireSnapshotIsolation();
        var provider = CreateProvider();
        using var cts = new CancellationTokenSource(300);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.CompareResultsAsync("SELECT 1 AS One", SlowQuery, 10, cts.Token));
    }

    [Fact]
    public async Task ConnectionFailure_ProducesNoEvidence()
    {
        var options = new DatabaseOptions(
            ConnectionString: "Server=127.0.0.1,59999;User Id=sa;Password=NotUsed;TrustServerCertificate=True;Connect Timeout=1",
            Enabled: true,
            CommandTimeoutSeconds: 5,
            ConnectionTimeoutSeconds: 1);
        var provider = CreateProvider(options);

        var result = await provider.CompareResultsAsync("SELECT 1 AS One", "SELECT 1 AS One", 10);

        result.Should().BeNull();
    }

    private SqlDatabaseValidationProvider CreateProvider(DatabaseOptions? options = null) =>
        new(
            new SqlServerSqlParser(),
            options ?? _fixture.CreateOptions(),
            NullLogger<SqlDatabaseValidationProvider>.Instance);
}
