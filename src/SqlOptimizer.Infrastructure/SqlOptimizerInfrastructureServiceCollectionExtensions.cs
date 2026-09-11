using Microsoft.Extensions.DependencyInjection;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Application.Services.Validation;
using SqlOptimizer.Infrastructure.Database;
using SqlOptimizer.Infrastructure.Validation;

namespace SqlOptimizer.Infrastructure;

/// <summary>
/// Registers the SQL Server infrastructure providers for dependency
/// injection: the full <see cref="ISqlDatabaseProvider"/> (schema, execution
/// plans, guarded read-only execution) and the focused database
/// validation/metadata providers. All database providers are scoped (they
/// depend on scoped services such as the parser and hold per-scope state
/// such as the metadata cache) and depend only on singleton options, so no
/// singleton here ever depends on a scoped service.
/// </summary>
public static class SqlOptimizerInfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQL Server database providers. The registration is safe
    /// in both modes: when the database is not enabled (or no connection
    /// string is configured) the providers are inert (
    /// <c>IsAvailable</c>=false / empty results / controlled errors), so the
    /// application runs fully without a database.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The database options (already bound from configuration; register the same instance as a singleton if not done yet).</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var enabled = options.Enabled && !string.IsNullOrWhiteSpace(options.ConnectionString);

        // Live schema metadata. The concrete type is registered in both modes
        // because SqlServerDatabaseProvider consumes it directly in its
        // constructor (it stays inert when the database is disabled). The
        // interface is registered only when the database is enabled,
        // preserving the M6/M7 contract that consumers (such as SqlValidator)
        // see no metadata provider at all without a configured database.
        services.AddScoped<SqlDatabaseMetadataProvider>();

        if (enabled)
        {
            services.AddScoped<IDatabaseValidationProvider, SqlDatabaseValidationProvider>();
            services.AddScoped<IDatabaseMetadataProvider>(sp => sp.GetRequiredService<SqlDatabaseMetadataProvider>());
        }
        else
        {
            services.AddScoped<IDatabaseValidationProvider, UnavailableDatabaseValidationProvider>();
        }

        // Full database provider (M8): schema, estimated execution plans and
        // guarded read-only execution. It stays inert until a connection
        // string is configured AND Database:Enabled is true.
        services.AddScoped<ISqlDatabaseProvider, SqlServerDatabaseProvider>();

        return services;
    }
}