using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SqlOptimizer.Api.Tests.TestSupport;

/// <summary>
/// Creates hermetic <see cref="WebApplicationFactory{Program}"/> instances for
/// the M7 API tests. Ambient environment variables that could leak into the
/// application configuration are cleared before each factory is created; the
/// per-test configuration is then supplied as process environment variables
/// (a documented configuration source read by <c>WebApplicationBuilder</c>).
/// Because environment variables are process-global, the test collections in
/// this assembly run without parallelization (see <c>AssemblyInfo.cs</c>).
/// No Docker/SQL Server is ever required by these tests.
/// </summary>
public static class ApiFactory
{
    /// <summary>Environment variables cleared before every factory creation.</summary>
    private static readonly string[] AmbientVariables =
    [
        "Api__RequireApiKey",
        "Api__ApiKey",
        "Api__ApiKeyHeaderName",
        "Api__MaxSqlLengthChars",
        "Api__MaxRequestBodyBytes",
        "Api__EnableSwagger",
        "Api__EnableRateLimiting",
        "Api__RateLimitPerMinute",
        "Api__HealthCheckTimeoutSeconds",
        "Database__ConnectionString",
        "Database__Enabled",
        "SqlOptimizer__EnableRuntimeValidation"
    ];

    /// <summary>
    /// Creates a factory with optional per-factory configuration settings
    /// (for example <c>Api__RequireApiKey=true</c>) and host configuration.
    /// Settings are applied as environment variables because
    /// <c>WebApplicationFactory</c> <c>UseSetting</c> values never reach the
    /// <c>WebApplicationBuilder</c> configuration in minimal hosting.
    /// </summary>
    /// <param name="settings">Configuration key/value pairs applied to this factory only.</param>
    /// <param name="configure">Optional additional IWebHostBuilder configuration.</param>
    public static WebApplicationFactory<Program> Create(
        IReadOnlyDictionary<string, string>? settings = null,
        Action<IWebHostBuilder>? configure = null)
    {
        foreach (var name in AmbientVariables)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        if (settings is not null)
        {
            foreach (var (key, value) in settings)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        var factory = new WebApplicationFactory<Program>();
        if (configure is not null)
        {
            factory = factory.WithWebHostBuilder(configure);
        }

        return factory;
    }
}
