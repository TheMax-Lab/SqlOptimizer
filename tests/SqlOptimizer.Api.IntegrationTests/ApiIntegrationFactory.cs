using Microsoft.AspNetCore.Mvc.Testing;
using SqlOptimizer.Infrastructure.IntegrationTests;

namespace SqlOptimizer.Api.IntegrationTests;

/// <summary>
/// Creates WebApplicationFactory instances wired to the shared disposable SQL
/// Server: the database provider is configured and enabled, and runtime
/// validation is switched on so the full M6 evidence chain (structure +
/// semantic risk + live metadata + runtime result comparison) runs over HTTP.
/// </summary>
public static class ApiIntegrationFactory
{
    /// <summary>
    /// Creates a factory bound to the live SQL Server of the shared fixture.
    /// </summary>
    /// <param name="fixture">Shared disposable SQL Server.</param>
    /// <param name="extraSettings">Additional per-factory configuration overrides.</param>
    public static WebApplicationFactory<Program> CreateLive(
        TestSqlServerFixture fixture,
        IReadOnlyDictionary<string, string>? extraSettings = null)
    {
        // A minimal-hosting app (WebApplication.CreateBuilder) builds its own
        // configuration, so WebApplicationFactory's UseSetting and
        // ConfigureAppConfiguration/AddInMemoryCollection overrides do NOT reach
        // builder.Configuration (verified live: Database stayed disabled and
        // runtime validation reported NotExecuted). The environment variables
        // provider, however, is always part of builder.Configuration, so the
        // live configuration is supplied as real environment variables - the
        // only override mechanism that reliably reaches the app under minimal
        // hosting. Every test in this assembly is a live integration test bound
        // to the same shared fixture, so process-level variables are safe and
        // idempotent here, and they never make the production API require SQL
        // Server (defaults stay database-less).
        Environment.SetEnvironmentVariable("Database__ConnectionString", fixture.ConnectionString);
        Environment.SetEnvironmentVariable("Database__Enabled", "true");
        Environment.SetEnvironmentVariable("SqlOptimizer__EnableRuntimeValidation", "true");

        if (extraSettings is not null)
        {
            foreach (var (key, value) in extraSettings)
            {
                // ':' is the configuration section separator; the environment
                // variables provider uses '__' as its separator, so translate.
                Environment.SetEnvironmentVariable(key.Replace(":", "__"), value);
            }
        }

        return new WebApplicationFactory<Program>();
    }
}
