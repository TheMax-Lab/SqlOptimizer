using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Infrastructure.Database;
using SqlOptimizer.Infrastructure.Parsing;
using SqlOptimizer.Infrastructure.Validation;
using Xunit;

namespace SqlOptimizer.Infrastructure.IntegrationTests;

/// <summary>
/// M8 live integration tests for <see cref="SqlServerDatabaseProvider"/>
/// against a disposable SQL Server (Testcontainers): full schema metadata,
/// estimated execution plans, guarded read-only execution, safety-guard
/// rejections and controlled errors. Tagged with the "Integration" category;
/// without Docker the shared fixture fails with an explicit "integration
/// environment unavailable" message and no live verification is claimed.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SqlServerDatabaseProviderIntegrationTests
{
    private readonly TestSqlServerFixture _fixture;

    /// <summary>Creates a new test instance bound to the shared disposable SQL Server.</summary>
    /// <param name="fixture">Shared disposable SQL Server.</param>
    public SqlServerDatabaseProviderIntegrationTests(TestSqlServerFixture fixture) =>
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public async Task Schema_ReturnsAllTablesColumnsTypesAndRowCounts()
    {
        var schema = await CreateProvider().GetSchemaAsync();

        schema.Tables.Select(t => t.Name).Should().BeEquivalentTo("Customers", "Orders", "Bench");

        var customers = schema.FindTable("Customers")!;
        customers.Schema.Should().Be("dbo");
        customers.Columns.Select(c => c.Name).Should().ContainInOrder("Id", "Name", "City");
        customers.FindColumn("Id")!.Should().Be(new DatabaseColumn("Id", "int", Nullable: false, PrimaryKey: true));
        customers.FindColumn("Name")!.DataType.Should().Be("nvarchar(100)");
        customers.FindColumn("City")!.Nullable.Should().BeTrue();
        customers.EstimatedRowCount.Should().Be(4);

        var bench = schema.FindTable("Bench")!;
        bench.EstimatedRowCount.Should().Be(100);
    }

    [Fact]
    public async Task Schema_ReportsClusteredPrimaryKeyAndNonClusteredIndex()
    {
        var schema = await CreateProvider().GetSchemaAsync();
        var customers = schema.FindTable("Customers")!;

        var clusteredPk = customers.Indexes.Single(i => i.Clustered);
        clusteredPk.Unique.Should().BeTrue();
        clusteredPk.KeyColumns.Should().BeEquivalentTo(["Id"]);

        var cityIndex = customers.Indexes.Single(i => string.Equals(i.Name, "IX_Customers_City", StringComparison.OrdinalIgnoreCase));
        cityIndex.KeyColumns.Should().ContainSingle().Which.Should().Be("City");
        cityIndex.Unique.Should().BeFalse();
        cityIndex.Clustered.Should().BeFalse();
    }

    [Fact]
    public async Task ExecutionPlan_ReturnsOperatorsForSelect()
    {
        var plan = await CreateProvider().GetExecutionPlanAsync("SELECT c.Id, c.Name FROM dbo.Customers c");

        plan.RawPlan.Should().Contain("ShowPlanXML");
        plan.Operators.Should().NotBeEmpty();
        plan.Operators.Should().Contain(op =>
            op.ObjectName != null && op.ObjectName.Contains("Customers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecutionPlan_InvalidSql_ThrowsSqlDatabaseException()
    {
        var provider = CreateProvider();

        var act = () => provider.GetExecutionPlanAsync("SELEKT 1 FROM NOWHERE");

        var assertion = await act.Should().ThrowAsync<SqlDatabaseException>();

        assertion.Which.Message.Should().NotContain(_fixture.ConnectionString);
    }

    [Fact]
    public async Task Execute_Select_ReturnsRowCountAndColumns()
    {
        _fixture.RequireSnapshotIsolation();
        var result = await CreateProvider().ExecuteAsync("SELECT Id, Name FROM dbo.Customers ORDER BY Id");

        result.RowsReturned.Should().Be(4);
        result.ColumnNames.Should().BeEquivalentTo(["Id", "Name"]);
        result.Duration.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task Execute_ResultIsBoundedByRowCap()
    {
        _fixture.RequireSnapshotIsolation();
        var provider = CreateProvider(maxRowsForComparison: 10);

        var result = await provider.ExecuteAsync("SELECT N FROM dbo.Bench");

        result.RowsReturned.Should().Be(10);
    }

    [Theory]
    [InlineData("INSERT INTO dbo.Customers (Id, Name) VALUES (999, N'x')")]
    [InlineData("UPDATE dbo.Customers SET Name = N'x'")]
    [InlineData("DELETE FROM dbo.Customers")]
    [InlineData("MERGE INTO dbo.Customers c USING dbo.Orders o ON c.Id = o.OrderId WHEN MATCHED THEN UPDATE SET Name = N'x'")]
    [InlineData("DROP TABLE dbo.Customers")]
    [InlineData("ALTER TABLE dbo.Customers ADD X INT")]
    [InlineData("CREATE TABLE T (Id INT)")]
    [InlineData("TRUNCATE TABLE dbo.Customers")]
    public async Task Execute_ForbiddenStatements_AreRejected(string sql)
    {
        var provider = CreateProvider();

        var act = () => provider.ExecuteAsync(sql);

        await act.Should().ThrowAsync<SqlSafetyException>();
    }

    [Fact]
    public async Task Execute_ForbiddenStatements_HaveNoSideEffects()
    {
        var provider = CreateProvider();

        foreach (var sql in new[]
        {
            "INSERT INTO dbo.Customers (Id, Name) VALUES (999, N'x')",
            "UPDATE dbo.Customers SET Name = N'x'",
            "DELETE FROM dbo.Customers",
            "TRUNCATE TABLE dbo.Customers",
            "DROP TABLE dbo.Customers"
        })
        {
            var act = () => provider.ExecuteAsync(sql);

            await act.Should().ThrowAsync<SqlSafetyException>();
        }

        var count = Convert.ToInt64(await _fixture.QueryScalarAsync("SELECT COUNT(*) FROM dbo.Customers"));
        count.Should().Be(4);
    }

    [Fact]
    public async Task Execute_UnparseableSql_IsRejectedFailClosed()
    {
        var provider = CreateProvider();

        var act = () => provider.ExecuteAsync("SELEKT 1");

        await act.Should().ThrowAsync<SqlSafetyException>();
    }

    [Fact]
    public async Task Provider_WhenDisabled_ReturnsEmptySchema_AndRefusesExecution()
    {
        var options = new DatabaseOptions(ConnectionString: _fixture.ConnectionString, Enabled: false);
        var provider = new SqlServerDatabaseProvider(
            new SqlServerSqlParser(),
            new SqlDatabaseMetadataProvider(options, NullLogger<SqlDatabaseMetadataProvider>.Instance),
            options,
            new SqlOptimizerOptions(),
            NullLogger<SqlServerDatabaseProvider>.Instance);

        (await provider.GetSchemaAsync()).Tables.Should().BeEmpty();

        Func<Task> planAct = () => provider.GetExecutionPlanAsync("SELECT 1");
        await planAct.Should().ThrowAsync<SqlDatabaseException>();

        Func<Task> executeAct = () => provider.ExecuteAsync("SELECT 1");
        await executeAct.Should().ThrowAsync<SqlDatabaseException>();
    }

    private SqlServerDatabaseProvider CreateProvider(
        int commandTimeoutSeconds = 30,
        int maxRowsForComparison = 1_000)
    {
        var options = _fixture.CreateOptions(commandTimeoutSeconds, maxRowsForComparison: maxRowsForComparison);
        return new SqlServerDatabaseProvider(
            new SqlServerSqlParser(),
            new SqlDatabaseMetadataProvider(options, NullLogger<SqlDatabaseMetadataProvider>.Instance),
            options,
            new SqlOptimizerOptions(),
            NullLogger<SqlServerDatabaseProvider>.Instance);
    }
}