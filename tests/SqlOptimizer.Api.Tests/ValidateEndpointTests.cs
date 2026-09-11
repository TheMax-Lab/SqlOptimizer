using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SqlOptimizer.Api.Tests.TestSupport;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Application.Services.Validation;
using SqlOptimizer.Domain.Parsing;
using Xunit;

namespace SqlOptimizer.Api.Tests;

/// <summary>
/// M7 offline tests for POST /api/v1/validate: the domain ValidationResult is
/// returned as-is with 200 for every status (Passed, Failed, Inconclusive);
/// the API never escalates or fabricates an outcome, and without a database
/// it can never claim semantic equivalence.
/// </summary>
public sealed class ValidateEndpointTests
{
    [Fact]
    public async Task EquivalentSqlWithoutRuntimeEvidence_NeverClaimsSemanticEquivalence()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/validate", new
        {
            originalSql = "SELECT Id, Name FROM dbo.Customers ORDER BY Id",
            candidateSql = "SELECT Id, Name FROM dbo.Customers ORDER BY Id",
            compareResults = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("syntaxValid").GetBoolean().Should().BeTrue();
        root.GetProperty("semanticallyEquivalent").GetBoolean().Should().BeFalse(
            "equivalence requires a real runtime comparison; no database is configured here");
        root.GetProperty("status").GetString().Should().BeOneOf("Passed", "Inconclusive", "NotExecuted", "NotRequested", "Failed");
    }

    [Fact]
    public async Task CompareResultsWithoutDatabase_Returns200WithExplicitLimitation()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/validate", new
        {
            originalSql = "SELECT Id FROM dbo.Customers ORDER BY Id",
            candidateSql = "SELECT Id FROM dbo.Customers ORDER BY Id",
            compareResults = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("semanticallyEquivalent").GetBoolean().Should().BeFalse();
        var limitations = root.GetProperty("limitations").EnumerateArray().Select(e => e.GetString()!).ToList();
        limitations.Should().Contain(l => l.Contains("database", StringComparison.OrdinalIgnoreCase),
            "the missing database must be reported as an explicit limitation, not hidden");
    }

    [Fact]
    public async Task SemanticRiskCandidate_Returns200WithInconclusiveStatus()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        // NOT IN over a nullable column vs NOT EXISTS: statically inconclusive
        // (NULL semantics), matching the documented Application behavior.
        var response = await client.PostAsJsonAsync("/api/v1/validate", new
        {
            originalSql = "SELECT Id FROM dbo.Customers c WHERE c.City NOT IN (SELECT City FROM dbo.Excluded)",
            candidateSql = "SELECT Id FROM dbo.Customers c WHERE NOT EXISTS (SELECT 1 FROM dbo.Excluded x WHERE x.City = c.City)",
            compareResults = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("Inconclusive");
        root.GetProperty("semanticallyEquivalent").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task WriteStatementCandidate_Returns200WithFailedStatus()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/validate", new
        {
            originalSql = "SELECT Id FROM dbo.Customers ORDER BY Id",
            candidateSql = "DELETE FROM dbo.Customers",
            compareResults = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("status").GetString().Should().Be("Failed");
    }

    [Fact]
    public async Task UnparseableOriginalSql_Returns400WithSqlParseError()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/validate", new
        {
            originalSql = "SELEC 1 FRO",
            candidateSql = "SELECT Id FROM dbo.Customers"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("SQL_PARSE_ERROR");
    }

    [Fact]
    public async Task EmptyOriginalSql_Returns400WithSqlInvalidInput()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/validate", new
        {
            originalSql = "",
            candidateSql = "SELECT Id FROM dbo.Customers"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("SQL_INVALID_INPUT");
    }

    [Fact]
    public async Task RequestIsMappedToTheApplicationValidator()
    {
        var captured = new DelegatingSqlValidator?[1];
        await using var factory = WithValidatorProbe(captured);
        using var client = factory.CreateDefaultClient();

        var response = await client.PostAsJsonAsync("/api/v1/validate", new
        {
            originalSql = "SELECT Id FROM dbo.Customers ORDER BY Id",
            candidateSql = "SELECT Id FROM dbo.Customers ORDER BY Id",
            compareResults = true,
            maxRowsForComparison = 250
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        captured[0].Should().NotBeNull("the endpoint must delegate to the Application validator");
        var probe = captured[0]!;
        probe.CallCount.Should().Be(1, "the endpoint must delegate to the Application validator exactly once");
        probe.LastRequest.Should().NotBeNull();
        var request = probe.LastRequest!;
        request.CompareResults.Should().BeTrue();
        request.MaxRowsForComparison.Should().Be(250);
        request.OriginalSql.Should().Be("SELECT Id FROM dbo.Customers ORDER BY Id");
    }

    private static WebApplicationFactory<Program> WithValidatorProbe(DelegatingSqlValidator?[] captured) =>
        ApiFactory.Create(configure: builder => builder.ConfigureTestServices(services =>
            // The probe is scoped like the real service, but the first instance
            // created (by the request scope) is captured so the test can observe
            // the exact instance the endpoint used: resolving a fresh scope
            // would create a new instance with an empty counter.
            services.AddScoped<ISqlValidator>(sp => captured[0] ??= new DelegatingSqlValidator(
                new SqlValidator(
                    sp.GetRequiredService<ISqlParser>(),
                    sp.GetRequiredService<SqlOptimizerOptions>(),
                    sp.GetRequiredService<IDatabaseValidationProvider>(),
                    sp.GetService<IDatabaseMetadataProvider>())))));
}
