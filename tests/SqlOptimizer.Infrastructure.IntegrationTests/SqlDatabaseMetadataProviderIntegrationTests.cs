using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Infrastructure.Validation;
using Xunit;

namespace SqlOptimizer.Infrastructure.IntegrationTests;

/// <summary>
/// M6 live integration tests for <see cref="SqlDatabaseMetadataProvider"/>
/// against a disposable SQL Server (Testcontainers). Tagged with the
/// "Integration" category; without Docker the shared fixture fails with an
/// explicit "integration environment unavailable" message.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SqlDatabaseMetadataProviderIntegrationTests
{
    private readonly TestSqlServerFixture _fixture;

    /// <summary>Creates a new test instance bound to the shared disposable SQL Server.</summary>
    /// <param name="fixture">Shared disposable SQL Server.</param>
    public SqlDatabaseMetadataProviderIntegrationTests(TestSqlServerFixture fixture) =>
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public async Task Metadata_ReportsTablesColumnsTypesNullabilityOrderAndRowCount()
    {
        var schema = await CreateProvider().GetTablesSchemaAsync(["dbo.Customers"]);

        schema.Should().NotBeNull();
        var table = schema!.Tables.Single();
        table.Schema.Should().Be("dbo");
        table.Name.Should().Be("Customers");
        table.Columns.Select(c => c.Name).Should().ContainInOrder("Id", "Name", "City");

        var id = table.FindColumn("Id");
        id.Should().NotBeNull();
        id!.DataType.Should().Be("int");
        id.Nullable.Should().BeFalse();
        id.PrimaryKey.Should().BeTrue();

        var name = table.FindColumn("Name")!;
        name.DataType.Should().Be("nvarchar(100)");
        name.Nullable.Should().BeFalse();
        name.PrimaryKey.Should().BeFalse();

        var city = table.FindColumn("City")!;
        city.DataType.Should().Be("nvarchar(50)");
        city.Nullable.Should().BeTrue();

        table.EstimatedRowCount.Should().Be(4);
    }

    [Fact]
    public async Task Metadata_ReportsPrimaryKeysAndIndexes()
    {
        var schema = await CreateProvider().GetTablesSchemaAsync(["dbo.Customers"]);
        var table = schema!.Tables.Single();

        var clusteredPk = table.Indexes.Single(i => i.Clustered);
        clusteredPk.Unique.Should().BeTrue();
        clusteredPk.KeyColumns.Should().BeEquivalentTo(["Id"]);

        var cityIndex = table.Indexes
            .Single(i => string.Equals(i.Name, "IX_Customers_City", StringComparison.OrdinalIgnoreCase));
        cityIndex.KeyColumns.Should().ContainSingle().Which.Should().Be("City");
        cityIndex.Unique.Should().BeFalse();
        cityIndex.Clustered.Should().BeFalse();
    }

    [Fact]
    public async Task Metadata_ReturnsOnlyRequestedTables()
    {
        var schema = await CreateProvider().GetTablesSchemaAsync(["dbo.Customers", "dbo.Orders"]);

        schema.Should().NotBeNull();
        schema!.Tables.Select(t => t.Name).Should().BeEquivalentTo("Customers", "Orders");
    }

    [Fact]
    public async Task Metadata_UnknownTable_IsNeverFabricated()
    {
        var schema = await CreateProvider().GetTablesSchemaAsync(["dbo.DoesNotExist"]);

        schema.Should().NotBeNull();
        schema!.Tables.Should().BeEmpty();
    }

    [Fact]
    public async Task Metadata_WhenDisabled_ReturnsNull()
    {
        var provider = new SqlDatabaseMetadataProvider(
            new DatabaseOptions(ConnectionString: _fixture.ConnectionString, Enabled: false),
            NullLogger<SqlDatabaseMetadataProvider>.Instance);

        (await provider.GetTablesSchemaAsync(["dbo.Customers"])).Should().BeNull();
    }

    private SqlDatabaseMetadataProvider CreateProvider() =>
        new(_fixture.CreateOptions(), NullLogger<SqlDatabaseMetadataProvider>.Instance);
}
