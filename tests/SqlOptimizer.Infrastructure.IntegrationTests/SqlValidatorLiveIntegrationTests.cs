using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Application.Services.Validation;
using SqlOptimizer.Infrastructure.Parsing;
using SqlOptimizer.Infrastructure.Validation;
using Xunit;

namespace SqlOptimizer.Infrastructure.IntegrationTests;

/// <summary>
/// M6 end-to-end integration tests: the real <see cref="SqlValidator"/>
/// pipeline (structure + semantic risk + live metadata + runtime result
/// comparison) against a disposable SQL Server. Tagged with the "Integration"
/// category; without Docker the shared fixture fails with an explicit
/// "integration environment unavailable" message.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SqlValidatorLiveIntegrationTests
{
    private readonly TestSqlServerFixture _fixture;

    /// <summary>Creates a new test instance bound to the shared disposable SQL Server.</summary>
    /// <param name="fixture">Shared disposable SQL Server.</param>
    public SqlValidatorLiveIntegrationTests(TestSqlServerFixture fixture) =>
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public async Task LiveMetadataAndRuntimeEquality_ProveSemanticEquivalence()
    {
        _fixture.RequireSnapshotIsolation();
        var result = await CreateValidator().ValidateAsync(new SqlValidationRequest(
            OriginalSql: "SELECT * FROM dbo.Customers ORDER BY Id",
            CandidateSql: "SELECT Id, Name, City FROM dbo.Customers ORDER BY Id",
            CompareResults: true,
            MaxRowsForComparison: 1000));

        result.Status.Should().Be(ValidationStatus.Passed);
        result.SemanticallyEquivalent.Should().BeTrue();
        result.ValidationConfidence.Should().BeGreaterThan(0.9);
        result.Evidence.Should().Contain(e => e.Contains("Live schema metadata"));
    }

    [Fact]
    public async Task RuntimeMismatch_IsFailedAndNotEquivalent()
    {
        _fixture.RequireSnapshotIsolation();
        var result = await CreateValidator().ValidateAsync(new SqlValidationRequest(
            OriginalSql: "SELECT Id, Name FROM dbo.Customers WHERE Id < 100 ORDER BY Id",
            CandidateSql: "SELECT Id, Name FROM dbo.Customers WHERE Id < 2 ORDER BY Id",
            CompareResults: true,
            MaxRowsForComparison: 1000));

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SemanticallyEquivalent.Should().BeFalse();
        result.SyntaxValid.Should().BeTrue();
    }

    [Fact]
    public async Task CountStarVsCountNullableColumn_IsNotEquivalentWithLiveMetadata()
    {
        // dbo.Orders.Total is nullable (one seeded NULL): live nullability
        // metadata plus the runtime comparison both prove the difference.
        _fixture.RequireSnapshotIsolation();
        var result = await CreateValidator().ValidateAsync(new SqlValidationRequest(
            OriginalSql: "SELECT COUNT(*) AS C FROM dbo.Orders",
            CandidateSql: "SELECT COUNT(Total) AS C FROM dbo.Orders",
            CompareResults: true,
            MaxRowsForComparison: 1000));

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SemanticallyEquivalent.Should().BeFalse();
        result.Evidence.Should().Contain(e => e.Contains("Live schema metadata"));
    }

    [Fact]
    public async Task ReadWriteCandidate_IsFailedWithoutExecution()
    {
        var result = await CreateValidator().ValidateAsync(new SqlValidationRequest(
            OriginalSql: "SELECT Id FROM dbo.Customers ORDER BY Id",
            CandidateSql: "DELETE FROM dbo.Customers",
            CompareResults: true,
            MaxRowsForComparison: 1000));

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SemanticallyEquivalent.Should().BeFalse();
        var customers = await _fixture.QueryScalarAsync("SELECT COUNT(*) FROM dbo.Customers");
        customers.Should().Be(4L);
    }

    private SqlValidator CreateValidator()
    {
        var parser = new SqlServerSqlParser();
        var options = _fixture.CreateOptions();
        return new SqlValidator(
            parser,
            new SqlOptimizerOptions { EnableRuntimeValidation = true },
            new SqlDatabaseValidationProvider(parser, options, NullLogger<SqlDatabaseValidationProvider>.Instance),
            new SqlDatabaseMetadataProvider(options, NullLogger<SqlDatabaseMetadataProvider>.Instance));
    }
}
