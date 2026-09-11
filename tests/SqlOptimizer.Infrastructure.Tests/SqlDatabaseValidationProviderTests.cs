using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Infrastructure.Parsing;
using SqlOptimizer.Infrastructure.Validation;
using Xunit;

namespace SqlOptimizer.Infrastructure.Tests;

/// <summary>
/// Offline behavior of the SQL Server validation provider: the read-only
/// gate, parameter/ORDER BY refusals and connection failures must all yield
/// null ("no evidence"), never a fabricated result and never an exception
/// (cancellation excepted). No database is required for these tests. A
/// connection-factory probe additionally proves that unsafe statements are
/// rejected before any connection is ever opened.
/// </summary>
public class SqlDatabaseValidationProviderTests
{
    /// <summary>
    /// Connection to a closed loopback port: connection refused fails fast,
    /// so these tests stay offline.
    /// </summary>
    private const string UnreachableConnectionString =
        "Server=127.0.0.1,1;User Id=sa;Password=unused;TrustServerCertificate=True;";

    private static DatabaseOptions ConfiguredOptions() => new(
        ConnectionString: UnreachableConnectionString,
        Enabled: true,
        CommandTimeoutSeconds: 5,
        ConnectionTimeoutSeconds: 2);

    private static SqlDatabaseValidationProvider CreateProvider(
        DatabaseOptions? options = null,
        Func<SqlConnection>? connectionFactory = null) =>
        new(new SqlServerSqlParser(), options ?? ConfiguredOptions(), NullLogger<SqlDatabaseValidationProvider>.Instance, connectionFactory);

    [Fact]
    public async Task UnconfiguredProvider_IsNotAvailable_AndReturnsNull()
    {
        var provider = new SqlDatabaseValidationProvider(
            new SqlServerSqlParser(), new DatabaseOptions(), NullLogger<SqlDatabaseValidationProvider>.Instance);

        provider.IsAvailable.Should().BeFalse();

        var result = await provider.CompareResultsAsync("SELECT 1", "SELECT 1", 100);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DmlCandidate_IsRefusedBeforeAnyConnection()
    {
        var provider = CreateProvider();

        var result = await provider.CompareResultsAsync("SELECT 1", "INSERT INTO T (Id) VALUES (1)", 100);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DmlOriginal_IsRefusedBeforeAnyConnection()
    {
        var provider = CreateProvider();

        var result = await provider.CompareResultsAsync("DELETE FROM T", "SELECT 1", 100);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ParameterizedSql_IsRefused_ValuesNeverFabricated()
    {
        var provider = CreateProvider();

        var result = await provider.CompareResultsAsync(
            "SELECT Id FROM T WHERE Id = @Id",
            "SELECT Id FROM T WHERE Id = @Id",
            100);

        result.Should().BeNull();
    }

    [Fact]
    public async Task OrderByPresenceMismatch_IsRefused()
    {
        var provider = CreateProvider();

        var result = await provider.CompareResultsAsync(
            "SELECT Id FROM T ORDER BY Id",
            "SELECT Id FROM T",
            100);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ConnectionFailure_ReturnsNull_NeverThrows()
    {
        var provider = CreateProvider();

        var result = await provider.CompareResultsAsync("SELECT 1", "SELECT 1", 10);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var provider = CreateProvider();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => provider.CompareResultsAsync("SELECT 1", "SELECT 1", 10, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void ProviderReportsSqlServerDialect()
    {
        var provider = CreateProvider();

        provider.Dialect.Should().Be(SqlDialect.SqlServer);
        provider.IsAvailable.Should().BeTrue();
    }

    /// <summary>
    /// Connection factory probe: counts how many times the provider actually
    /// creates a connection, proving that guarded statements never reach the
    /// database.
    /// </summary>
    private sealed class ConnectionProbe
    {
        public int Attempts { get; private set; }

        public SqlConnection Create()
        {
            Attempts++;
            return new SqlConnection(UnreachableConnectionString);
        }
    }

    [Theory]
    [InlineData("INSERT INTO T (Id) VALUES (1)")]
    [InlineData("UPDATE T SET Id = 2")]
    [InlineData("DELETE FROM T")]
    [InlineData("DROP TABLE T")]
    [InlineData("ALTER TABLE T ADD X INT")]
    [InlineData("CREATE TABLE T (Id INT)")]
    [InlineData("TRUNCATE TABLE T")]
    [InlineData("MERGE INTO T t USING S s ON t.Id = s.Id WHEN MATCHED THEN UPDATE SET Id = s.Id")]
    [InlineData("EXEC xp_cmdshell 'dir'")]
    [InlineData("SELECT 1; SELECT 2")]
    public async Task UnsafeCandidateSql_NeverOpensAConnection(string unsafeSql)
    {
        var probe = new ConnectionProbe();
        var provider = CreateProvider(connectionFactory: probe.Create);

        var result = await provider.CompareResultsAsync("SELECT 1", unsafeSql, 100);

        result.Should().BeNull();
        probe.Attempts.Should().Be(0, "unsafe SQL must be rejected by the read-only guard before any connection is opened");
    }

    [Theory]
    [InlineData("DELETE FROM T")]
    [InlineData("DROP TABLE T")]
    [InlineData("UPDATE T SET Id = 2")]
    [InlineData("SELECT 1; SELECT 2")]
    public async Task UnsafeOriginalSql_NeverOpensAConnection(string unsafeSql)
    {
        var probe = new ConnectionProbe();
        var provider = CreateProvider(connectionFactory: probe.Create);

        var result = await provider.CompareResultsAsync(unsafeSql, "SELECT 1", 100);

        result.Should().BeNull();
        probe.Attempts.Should().Be(0, "unsafe SQL must be rejected by the read-only guard before any connection is opened");
    }

    [Fact]
    public async Task ParameterizedSql_NeverOpensAConnection()
    {
        var probe = new ConnectionProbe();
        var provider = CreateProvider(connectionFactory: probe.Create);

        var result = await provider.CompareResultsAsync(
            "SELECT Id FROM T WHERE Id = @Id", "SELECT Id FROM T WHERE Id = @Id", 100);

        result.Should().BeNull();
        probe.Attempts.Should().Be(0, "no parameter values exist, so parameterized SQL is never executed");
    }

    [Fact]
    public async Task OrderByPresenceMismatch_NeverOpensAConnection()
    {
        var probe = new ConnectionProbe();
        var provider = CreateProvider(connectionFactory: probe.Create);

        var result = await provider.CompareResultsAsync(
            "SELECT Id FROM T ORDER BY Id", "SELECT Id FROM T", 100);

        result.Should().BeNull();
        probe.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task SafeSelects_ProceedToConnectionAttempt()
    {
        var probe = new ConnectionProbe();
        var provider = CreateProvider(connectionFactory: probe.Create);

        var result = await provider.CompareResultsAsync("SELECT 1", "SELECT 1", 10);

        // The guard lets the safe SELECTs through; the unreachable
        // connection then fails and the provider returns null (no evidence).
        result.Should().BeNull();
        probe.Attempts.Should().Be(1, "safe SELECTs must reach the execution path");
    }

    [Theory]
    [InlineData("insert into t (id) values (1)")]
    [InlineData("DeLeTe From T")]
    [InlineData("INSERT\tINTO t (id)\nVALUES (1)")]
    [InlineData("INSERT /*c*/ INTO t (id) VALUES (1)")]
    [InlineData("SELECT 1;-- hidden separator\nSELECT 2")]
    [InlineData("exec xp_cmdshell 'dir'")]
    public async Task MutatedUnsafeCandidateSql_NeverOpensAConnection(string unsafeSql)
    {
        // Casing, whitespace and comment mutations of unsafe statements must
        // be rejected by the read-only guard before any connection is opened.
        var probe = new ConnectionProbe();
        var provider = CreateProvider(connectionFactory: probe.Create);

        var result = await provider.CompareResultsAsync("SELECT 1", unsafeSql, 100);

        result.Should().BeNull();
        probe.Attempts.Should().Be(0, "mutated unsafe SQL must be rejected before any connection is opened");
    }

    [Fact]
    public async Task CommentedSafeSelects_StillProceedToConnectionAttempt()
    {
        // Comments in a safe SELECT do not change the statement kind: the
        // guard must not reject it, and execution is attempted exactly once.
        var probe = new ConnectionProbe();
        var provider = CreateProvider(connectionFactory: probe.Create);

        var result = await provider.CompareResultsAsync(
            "SELECT /* c */ 1 -- tail", "SELECT 1", 10);

        result.Should().BeNull("the unreachable connection produces no evidence");
        probe.Attempts.Should().Be(1, "a safe SELECT with comments must reach the execution path");
    }
}
