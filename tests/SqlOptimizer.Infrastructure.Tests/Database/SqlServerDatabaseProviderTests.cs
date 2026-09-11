using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Infrastructure.Database;
using SqlOptimizer.Infrastructure.Parsing;
using SqlOptimizer.Infrastructure.Validation;
using Xunit;

namespace SqlOptimizer.Infrastructure.Tests.Database;

/// <summary>
/// Offline behavior of <see cref="SqlServerDatabaseProvider"/>: dialect,
/// unconfigured (inert) mode, fail-closed safety-guard rejections, controlled
/// errors for database failures, cancellation propagation and secret
/// non-leakage. No SQL Server is required: configured-but-unreachable
/// connection strings fail fast on loopback.
/// </summary>
public class SqlServerDatabaseProviderTests
{
    /// <summary>Configured but unreachable SQL Server (loopback, closed port, 1s connect timeout).</summary>
    private const string UnreachableConnectionString =
        "Server=127.0.0.1,59999;Database=Test;User Id=sa;Password=NotARealSecret;TrustServerCertificate=True;Connect Timeout=1";

    private static SqlServerDatabaseProvider CreateProvider(
        string connectionString = "",
        bool enabled = false,
        int maxRowsForComparison = 1_000,
        int maxResultCells = 20_000,
        SqlOptimizerOptions? pipelineOptions = null) => new(
        new SqlServerSqlParser(),
        new SqlDatabaseMetadataProvider(
            new DatabaseOptions(
                ConnectionString: connectionString,
                Enabled: enabled,
                MaxRowsForComparison: maxRowsForComparison,
                MaxResultCells: maxResultCells),
            NullLogger<SqlDatabaseMetadataProvider>.Instance),
        new DatabaseOptions(
            ConnectionString: connectionString,
            Enabled: enabled,
            MaxRowsForComparison: maxRowsForComparison,
            MaxResultCells: maxResultCells),
        pipelineOptions ?? new SqlOptimizerOptions(),
        NullLogger<SqlServerDatabaseProvider>.Instance);

    [Fact]
    public void Dialect_IsSqlServer() =>
        CreateProvider().Dialect.Should().Be(SqlDialect.SqlServer);

    [Theory]
    [InlineData("", true)]
    [InlineData("", false)]
    [InlineData("Server=x;User Id=sa;Password=p", false)]
    [InlineData("Server=x;User Id=sa;Password=p", true)]
    public void IsConfigured_RequiresEnabledAndConnectionString(string connectionString, bool enabled) =>
        CreateProvider(connectionString, enabled).IsConfigured.Should().Be(
            enabled && !string.IsNullOrWhiteSpace(connectionString));

    [Fact]
    public async Task GetSchema_WhenNotConfigured_ReturnsEmptySchema()
    {
        var provider = CreateProvider();

        var schema = await provider.GetSchemaAsync();

        schema.Should().Be(DatabaseSchema.Empty);
        schema.Tables.Should().BeEmpty();
    }

    [Fact]
    public async Task GetExecutionPlan_WhenNotConfigured_ThrowsSqlDatabaseException()
    {
        var provider = CreateProvider();

        var act = () => provider.GetExecutionPlanAsync("SELECT 1");

        await act.Should().ThrowAsync<SqlDatabaseException>();
    }

    [Fact]
    public async Task Execute_WhenNotConfigured_ThrowsSqlDatabaseException()
    {
        var provider = CreateProvider();

        var act = () => provider.ExecuteAsync("SELECT 1");

        await act.Should().ThrowAsync<SqlDatabaseException>();
    }

    [Fact]
    public async Task GetExecutionPlan_EmptySql_ThrowsSqlInvalidInputException()
    {
        var provider = CreateProvider(UnreachableConnectionString, enabled: true);

        var act = () => provider.GetExecutionPlanAsync("   ");

        await act.Should().ThrowAsync<SqlInvalidInputException>();
    }

    [Theory]
    [InlineData("INSERT INTO dbo.Customers (Id, Name) VALUES (1, N'x')")]
    [InlineData("UPDATE dbo.Customers SET Name = N'x'")]
    [InlineData("DELETE FROM dbo.Customers")]
    [InlineData("MERGE INTO dbo.Customers c USING dbo.Orders o ON c.Id = o.OrderId WHEN MATCHED THEN UPDATE SET Name = N'x'")]
    [InlineData("DROP TABLE dbo.Customers")]
    [InlineData("ALTER TABLE dbo.Customers ADD X INT")]
    [InlineData("CREATE TABLE T (Id INT)")]
    [InlineData("TRUNCATE TABLE dbo.Customers")]
    public async Task Execute_ForbiddenStatements_AreRejectedBySafetyGuard(string sql)
    {
        // Configured + unreachable: if the guard did NOT reject, the call
        // would reach the database and fail with SqlDatabaseException instead.
        var provider = CreateProvider(UnreachableConnectionString, enabled: true);

        var act = () => provider.ExecuteAsync(sql);

        await act.Should().ThrowAsync<SqlSafetyException>();
    }

    [Theory]
    [InlineData("EXEC xp_cmdshell 'dir'")]
    [InlineData("BEGIN TRAN; SELECT 1; ROLLBACK")]
    [InlineData("SELECT Id INTO #T FROM dbo.Customers")]
    [InlineData("SELECT * FROM OPENROWSET('SQLOLEDB', 'server';'user';'pass', 'SELECT 1')")]
    [InlineData("SELECT * FROM [other-server].[mydb].[dbo].[Customers]")]
    [InlineData("SELECT 1; SELECT 2")]
    public async Task Execute_StateChangingConstructs_AreRejectedBySafetyGuard(string sql)
    {
        var provider = CreateProvider(UnreachableConnectionString, enabled: true);

        var act = () => provider.ExecuteAsync(sql);

        await act.Should().ThrowAsync<SqlSafetyException>();
    }

    [Theory]
    [InlineData("SELEKT 1")]
    [InlineData("SELECT FROM WHERE")]
    public async Task Execute_UnparseableSql_IsRejectedFailClosed(string sql)
    {
        // Cannot be classified as read-only: rejected, never executed.
        var provider = CreateProvider(UnreachableConnectionString, enabled: true);

        var act = () => provider.ExecuteAsync(sql);

        await act.Should().ThrowAsync<SqlSafetyException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Execute_EmptySql_IsRejected(string sql)
    {
        var provider = CreateProvider();

        var act = () => provider.ExecuteAsync(sql);

        await act.Should().ThrowAsync<SqlSafetyException>();
    }

    [Fact]
    public async Task Execute_AllowedSelect_PassesGuard_AndFailsControlledAgainstUnreachableDatabase()
    {
        var provider = CreateProvider(UnreachableConnectionString, enabled: true);

        var act = () => provider.ExecuteAsync("SELECT Id, Name FROM dbo.Customers");

        var assertion = await act.Should().ThrowAsync<SqlDatabaseException>();
        var exception = assertion.Which;

        // Connection details must never leak into the controlled error.
        exception.Message.Should()
            .NotContain("127.0.0.1")
            .And.NotContain("NotARealSecret")
            .And.NotContain("Server=");
    }

    [Fact]
    public async Task GetExecutionPlan_AllowedSelect_FailsControlledAgainstUnreachableDatabase()
    {
        var provider = CreateProvider(UnreachableConnectionString, enabled: true);

        var act = () => provider.GetExecutionPlanAsync("SELECT Id FROM dbo.Customers");

        var assertion = await act.Should().ThrowAsync<SqlDatabaseException>();
        var exception = assertion.Which;

        exception.Message.Should()
            .NotContain("127.0.0.1")
            .And.NotContain("NotARealSecret")
            .And.NotContain("Server=");
    }

    [Fact]
    public async Task GetSchema_ConfiguredButUnreachable_ThrowsSqlDatabaseException()
    {
        var provider = CreateProvider(UnreachableConnectionString, enabled: true);

        var act = () => provider.GetSchemaAsync();

        var assertion = await act.Should().ThrowAsync<SqlDatabaseException>();
        var exception = assertion.Which;

        exception.Message.Should()
            .NotContain("127.0.0.1")
            .And.NotContain("NotARealSecret")
            .And.NotContain("Server=");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationToken_Stopped_PropagatesAsOperationCanceled(bool execute)
    {
        var provider = CreateProvider(UnreachableConnectionString, enabled: true);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = execute
            ? () => provider.ExecuteAsync("SELECT 1", cts.Token)
            : () => provider.GetExecutionPlanAsync("SELECT 1", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Constructor_NullArguments_AreRejected()
    {
        var options = new DatabaseOptions();
        var pipeline = new SqlOptimizerOptions();

        Action a1 = () => new SqlServerDatabaseProvider(
            null!,
            new SqlDatabaseMetadataProvider(options, NullLogger<SqlDatabaseMetadataProvider>.Instance),
            options, pipeline, NullLogger<SqlServerDatabaseProvider>.Instance);
        a1.Should().Throw<ArgumentNullException>();

        Action a2 = () => new SqlServerDatabaseProvider(
            new SqlServerSqlParser(),
            null!,
            options, pipeline, NullLogger<SqlServerDatabaseProvider>.Instance);
        a2.Should().Throw<ArgumentNullException>();

        Action a3 = () => new SqlServerDatabaseProvider(
            new SqlServerSqlParser(),
            new SqlDatabaseMetadataProvider(options, NullLogger<SqlDatabaseMetadataProvider>.Instance),
            null!, pipeline, NullLogger<SqlServerDatabaseProvider>.Instance);
        a3.Should().Throw<ArgumentNullException>();

        Action a4 = () => new SqlServerDatabaseProvider(
            new SqlServerSqlParser(),
            new SqlDatabaseMetadataProvider(options, NullLogger<SqlDatabaseMetadataProvider>.Instance),
            options, null!, NullLogger<SqlServerDatabaseProvider>.Instance);
        a4.Should().Throw<ArgumentNullException>();

        Action a5 = () => new SqlServerDatabaseProvider(
            new SqlServerSqlParser(),
            new SqlDatabaseMetadataProvider(options, NullLogger<SqlDatabaseMetadataProvider>.Instance),
            options, pipeline, null!);
        a5.Should().Throw<ArgumentNullException>();
    }
}