using SqlOptimizer.Infrastructure.IntegrationTests;
using Xunit;

namespace SqlOptimizer.Api.IntegrationTests;

/// <summary>
/// xUnit collection that shares the single disposable SQL Server (same
/// fixture used by the M6 integration tests) across all M7 API integration
/// test classes. Parallelization inside the collection is disabled so the
/// shared database is never accessed by two tests at once. Without Docker the
/// fixture fails fast with an explicit "integration environment unavailable"
/// message, so these tests are skipped by the CI runner on non-Docker
/// machines rather than silently passing.
/// </summary>
[CollectionDefinition(ApiIntegrationCollection.Name, DisableParallelization = true)]
public sealed class ApiIntegrationCollection : ICollectionFixture<TestSqlServerFixture>
{
    /// <summary>Collection name shared by all M7 API integration test classes.</summary>
    public const string Name = "ApiSqlIntegration";
}
