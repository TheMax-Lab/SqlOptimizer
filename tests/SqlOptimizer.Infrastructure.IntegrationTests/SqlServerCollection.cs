using Xunit;

namespace SqlOptimizer.Infrastructure.IntegrationTests;

/// <summary>
/// xUnit collection that shares the single disposable SQL Server across all
/// M6 integration test classes. Parallelization inside the collection is
/// disabled so the shared database is never accessed by two tests at once.
/// </summary>
[CollectionDefinition(SqlServerCollection.Name, DisableParallelization = true)]
public sealed class SqlServerCollection : ICollectionFixture<TestSqlServerFixture>
{
    /// <summary>Collection name shared by all integration test classes.</summary>
    public const string Name = "M6SqlIntegration";
}
