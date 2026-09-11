using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.Services.Validation;
using SqlOptimizer.Infrastructure.Validation;
using Xunit;

namespace SqlOptimizer.Api.Tests;

/// <summary>
/// M6 conditional DI verification: without a configured database the
/// application starts normally and the safe unavailable provider stays
/// active; with a configured and enabled database the SQL Server validation
/// and live metadata providers replace it. Configuration is supplied through
/// environment variables (a documented configuration source); no live
/// database is needed, the configured tests resolve services only.
/// </summary>
public sealed class ConditionalDatabaseRegistrationTests
{
    private const string ConnectionStringVariable = "Database__ConnectionString";

    private const string EnabledVariable = "Database__Enabled";

    public ConditionalDatabaseRegistrationTests()
    {
        // Hermetic: every test starts without ambient database configuration.
        Environment.SetEnvironmentVariable(ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(EnabledVariable, null);
    }

    [Fact]
    public async Task WithoutDatabase_StartupSucceedsAndUnavailableProviderIsActive()
    {
        await using var factory = new WebApplicationFactory<Program>();

        (await factory.CreateDefaultClient().GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<IDatabaseValidationProvider>();

        provider.Should().BeOfType<UnavailableDatabaseValidationProvider>();
        provider.IsAvailable.Should().BeFalse();
        scope.ServiceProvider.GetService<IDatabaseMetadataProvider>().Should().BeNull();

        // The validator is still resolvable: the optional metadata provider
        // falls back to null and validation stays fully static.
        scope.ServiceProvider.GetRequiredService<ISqlValidator>().Should().BeOfType<SqlValidator>();
    }

    [Fact]
    public async Task WithConfiguredAndEnabledDatabase_SqlServerProvidersAreActive()
    {
        Environment.SetEnvironmentVariable(
            ConnectionStringVariable,
            "Server=test.local;User Id=sa;Password=NotUsed;TrustServerCertificate=True");
        Environment.SetEnvironmentVariable(EnabledVariable, "true");

        await using var factory = new WebApplicationFactory<Program>();

        (await factory.CreateDefaultClient().GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IDatabaseValidationProvider>()
            .Should().BeOfType<SqlDatabaseValidationProvider>();
        scope.ServiceProvider.GetRequiredService<IDatabaseMetadataProvider>()
            .Should().BeOfType<SqlDatabaseMetadataProvider>();
    }

    [Fact]
    public async Task WithConnectionStringButDisabled_UnavailableProviderRemainsActive()
    {
        Environment.SetEnvironmentVariable(
            ConnectionStringVariable,
            "Server=test.local;User Id=sa;Password=NotUsed;TrustServerCertificate=True");
        Environment.SetEnvironmentVariable(EnabledVariable, "false");

        await using var factory = new WebApplicationFactory<Program>();

        (await factory.CreateDefaultClient().GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IDatabaseValidationProvider>()
            .Should().BeOfType<UnavailableDatabaseValidationProvider>();
        scope.ServiceProvider.GetService<IDatabaseMetadataProvider>().Should().BeNull();
    }
}
