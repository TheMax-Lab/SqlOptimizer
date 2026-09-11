using FluentAssertions;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Domain.Common;
using Xunit;

namespace SqlOptimizer.Application.Tests.Validation;

/// <summary>
/// M12 boundary tests for the M10 validator: input limits, cancellation
/// token propagation to the validation provider, and the performance-claim
/// edges around execution durations. Offline and deterministic: only the
/// external database provider is a test double.
/// </summary>
public class SqlValidatorM12BoundaryTests
{
    [Fact]
    public async Task OriginalSqlExceedingMaxSqlLength_ThrowsSqlInvalidInputException()
    {
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { MaxSqlLength = 16 });

        var act = () => ValidationTestSupport.ValidateAsync(
            validator,
            new string('x', 17),
            "SELECT Id FROM Customers",
            compareResults: false);

        await act.Should().ThrowAsync<SqlInvalidInputException>();
    }

    [Fact]
    public async Task CallerToken_IsPropagatedToTheValidationProvider_AndNullResultStaysInconclusive()
    {
        // The provider is invoked with a real (non-default) token: it must
        // receive exactly the caller's token, and a null result (no
        // evidence) must stay Inconclusive, never Passed.
        var provider = new TokenObservingProvider();
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = true }, provider);

        using var cts = new CancellationTokenSource();
        var result = await validator.ValidateAsync(
            new SqlValidationRequest("SELECT Id FROM Customers", "SELECT Id FROM Customers", true, 100),
            cts.Token);

        provider.LastToken.Should().Be(cts.Token,
            "the caller's cancellation token must reach the database provider, not be dropped");
        result.Status.Should().Be(ValidationStatus.Inconclusive,
            "an attempted comparison that produced no evidence is Inconclusive, never Passed");
        result.SemanticallyEquivalent.Should().BeFalse();
        result.ImprovementPercentage.Should().BeNull();
    }

    [Fact]
    public async Task EquivalentWithZeroOriginalDuration_NoImprovementClaimed()
    {
        var provider = ValidationTestSupport.CreateFixedProvider(new DatabaseComparisonResult
        {
            ResultsEqual = true,
            OriginalRows = 10,
            CandidateRows = 10,
            OriginalDuration = TimeSpan.Zero,
            CandidateDuration = TimeSpan.FromMilliseconds(10)
        });
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = true }, provider);

        var result = await ValidationTestSupport.ValidateAsync(
            validator, "SELECT Id FROM Customers", "SELECT Id FROM Customers", compareResults: true);

        result.Status.Should().Be(ValidationStatus.Passed);
        result.SemanticallyEquivalent.Should().BeTrue();
        result.ImprovementPercentage.Should().BeNull(
            "a zero-length original duration makes any percentage undefined; no claim may be made");
    }

    [Fact]
    public async Task EquivalentWithoutDurations_PassedWithoutTimingClaims()
    {
        var provider = ValidationTestSupport.CreateFixedProvider(new DatabaseComparisonResult
        {
            ResultsEqual = true,
            OriginalRows = 10,
            CandidateRows = 10
            // no durations measured
        });
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = true }, provider);

        var result = await ValidationTestSupport.ValidateAsync(
            validator, "SELECT Id FROM Customers", "SELECT Id FROM Customers", compareResults: true);

        result.Status.Should().Be(ValidationStatus.Passed);
        result.SemanticallyEquivalent.Should().BeTrue();
        result.OriginalExecutionTime.Should().BeNull();
        result.OptimizedExecutionTime.Should().BeNull();
        result.ImprovementPercentage.Should().BeNull();
    }

    /// <summary>
    /// Provider double that records the exact cancellation token it is
    /// invoked with and returns no evidence (null).
    /// </summary>
    private sealed class TokenObservingProvider : IDatabaseValidationProvider
    {
        public SqlDialect Dialect => SqlDialect.SqlServer;

        public bool IsAvailable => true;

        public CancellationToken LastToken { get; private set; }

        public Task<DatabaseComparisonResult?> CompareResultsAsync(
            string originalSql,
            string candidateSql,
            int maxRowsForComparison,
            CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            return Task.FromResult<DatabaseComparisonResult?>(null);
        }
    }
}
