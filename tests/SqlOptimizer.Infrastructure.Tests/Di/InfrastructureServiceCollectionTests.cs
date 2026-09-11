using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Application.Services.Validation;
using SqlOptimizer.Domain.Parsing;
using SqlOptimizer.Infrastructure.Database;
using SqlOptimizer.Infrastructure.Parsing;
using SqlOptimizer.Infrastructure.Validation;
using Xunit;

namespace SqlOptimizer.Infrastructure.Tests.Di;

/// <summary>
/// AddInfrastructure registration contracts: enabled and disabled modes,
/// service types and scoped lifetimes.
/// </summary>
public class InfrastructureServiceCollectionTests
{
    private static ServiceProvider BuildProvider(DatabaseOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddSingleton(new SqlOptimizerOptions());
        services.AddScoped<ISqlParser, SqlServerSqlParser>();
        // Bare containers have no logging defaults: register null loggers so
        // the providers' ILogger<T> dependencies resolve in this unit test.
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddInfrastructure(options);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Enabled_RegistersSqlServerProviders()
    {
        using var provider = BuildProvider(new DatabaseOptions(ConnectionString: "Server=x;User Id=sa;Password=p", Enabled: true));
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        services.GetRequiredService<ISqlDatabaseProvider>().Should().BeOfType<SqlServerDatabaseProvider>();
        services.GetRequiredService<IDatabaseValidationProvider>().Should().BeOfType<SqlDatabaseValidationProvider>();
        services.GetRequiredService<IDatabaseMetadataProvider>().Should().BeOfType<SqlDatabaseMetadataProvider>();
    }

    [Fact]
    public void Disabled_RegistersUnavailableValidationProvider_KeepsOtherProviders()
    {
        using var provider = BuildProvider(new DatabaseOptions(ConnectionString: "Server=x;User Id=sa;Password=p", Enabled: false));
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        services.GetRequiredService<IDatabaseValidationProvider>().Should().BeOfType<UnavailableDatabaseValidationProvider>();
        services.GetRequiredService<ISqlDatabaseProvider>().Should().BeOfType<SqlServerDatabaseProvider>();
        services.GetRequiredService<SqlDatabaseMetadataProvider>().Should().BeOfType<SqlDatabaseMetadataProvider>();

        // M6/M7 contract: without an enabled database the metadata interface
        // is not registered, so consumers degrade exactly as before M8.
        services.GetService<IDatabaseMetadataProvider>().Should().BeNull();
    }

    [Fact]
    public void MissingConnectionString_RegistersUnavailableValidationProvider()
    {
        using var provider = BuildProvider(new DatabaseOptions(ConnectionString: "", Enabled: true));
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        services.GetRequiredService<IDatabaseValidationProvider>().Should().BeOfType<UnavailableDatabaseValidationProvider>();
    }

    [Fact]
    public void DatabaseProviders_AreScoped()
    {
        using var provider = BuildProvider(new DatabaseOptions(ConnectionString: "Server=x;User Id=sa;Password=p", Enabled: true));

        using (var scopeA = provider.CreateScope())
        using (var scopeB = provider.CreateScope())
        {
            // Same scope: the same instance (scoped lifetime).
            scopeA.ServiceProvider.GetRequiredService<ISqlDatabaseProvider>()
                .Should().BeSameAs(scopeA.ServiceProvider.GetRequiredService<ISqlDatabaseProvider>());

            // Different scopes: different instances.
            scopeA.ServiceProvider.GetRequiredService<ISqlDatabaseProvider>()
                .Should().NotBeSameAs(scopeB.ServiceProvider.GetRequiredService<ISqlDatabaseProvider>());
        }
    }

    [Fact]
    public void ResolvedProvider_ReportsConfiguredStateFromOptions()
    {
        using var enabledProvider = BuildProvider(new DatabaseOptions(ConnectionString: "Server=x;User Id=sa;Password=p", Enabled: true));
        using var enabledScope = enabledProvider.CreateScope();
        (enabledScope.ServiceProvider.GetRequiredService<ISqlDatabaseProvider>() as SqlServerDatabaseProvider)!.IsConfigured.Should().BeTrue();

        using var disabledProvider = BuildProvider(new DatabaseOptions(ConnectionString: "Server=x;User Id=sa;Password=p", Enabled: false));
        using var disabledScope = disabledProvider.CreateScope();
        (disabledScope.ServiceProvider.GetRequiredService<ISqlDatabaseProvider>() as SqlServerDatabaseProvider)!.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        var services = new ServiceCollection();

        Action a1 = () => services.AddInfrastructure(null!);
        a1.Should().Throw<ArgumentNullException>();

        Action a2 = () => ((IServiceCollection)null!).AddInfrastructure(new DatabaseOptions());
        a2.Should().Throw<ArgumentNullException>();
    }
}