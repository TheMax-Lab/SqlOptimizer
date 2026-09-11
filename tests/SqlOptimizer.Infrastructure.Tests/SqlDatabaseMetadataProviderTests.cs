using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Infrastructure.Validation;
using Xunit;

namespace SqlOptimizer.Infrastructure.Tests;

/// <summary>
/// Offline behavior of the live metadata provider: unconfigured providers
/// are not available and never fabricate metadata; connection failures
/// degrade to null. No database is required for these tests.
/// </summary>
public class SqlDatabaseMetadataProviderTests
{
    private const string UnreachableConnectionString =
        "Server=127.0.0.1,1;User Id=sa;Password=unused;TrustServerCertificate=True;";

    [Fact]
    public void UnconfiguredProvider_IsNotAvailable()
    {
        var provider = new SqlDatabaseMetadataProvider(new DatabaseOptions(), NullLogger<SqlDatabaseMetadataProvider>.Instance);

        provider.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void EnabledButMissingConnectionString_IsNotAvailable()
    {
        var provider = new SqlDatabaseMetadataProvider(
            new DatabaseOptions(Enabled: true), NullLogger<SqlDatabaseMetadataProvider>.Instance);

        provider.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task UnconfiguredProvider_ReturnsNullMetadata()
    {
        var provider = new SqlDatabaseMetadataProvider(new DatabaseOptions(), NullLogger<SqlDatabaseMetadataProvider>.Instance);

        var result = await provider.GetTablesSchemaAsync(["dbo.People"]);

        result.Should().BeNull();
    }

    [Fact]
    public async Task EmptyTableList_ReturnsEmptySchema_WithoutConnecting()
    {
        var provider = new SqlDatabaseMetadataProvider(
            new DatabaseOptions(ConnectionString: UnreachableConnectionString, Enabled: true, ConnectionTimeoutSeconds: 2),
            NullLogger<SqlDatabaseMetadataProvider>.Instance);

        var result = await provider.GetTablesSchemaAsync(Array.Empty<string>());

        result.Should().BeSameAs(DatabaseSchema.Empty);
    }

    [Fact]
    public async Task ConnectionFailure_ReturnsNull_NeverThrows()
    {
        var provider = new SqlDatabaseMetadataProvider(
            new DatabaseOptions(ConnectionString: UnreachableConnectionString, Enabled: true, ConnectionTimeoutSeconds: 2),
            NullLogger<SqlDatabaseMetadataProvider>.Instance);

        var result = await provider.GetTablesSchemaAsync(["dbo.People"]);

        result.Should().BeNull();
    }
}