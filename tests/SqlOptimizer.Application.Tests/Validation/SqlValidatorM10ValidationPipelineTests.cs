using FluentAssertions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Domain.Common;
using Xunit;

namespace SqlOptimizer.Application.Tests.Validation;

/// <summary>
/// M10 validation-pipeline contract tests. They pin down the status mapping
/// (NotExecuted when the requested runtime comparison could not be attempted
/// at all, Inconclusive when it was attempted but produced no evidence,
/// Failed on a proven mismatch), the rule that
/// <c>ImprovementPercentage</c> is only ever reported after semantic
/// equivalence has been proven, and the guarantee that no database access
/// happens unless the runtime stage is enabled and available. All tests are
/// offline and deterministic: the database is represented by the fixed
/// provider test double, which also records whether it was invoked.
/// </summary>
public class SqlValidatorM10ValidationPipelineTests
{
    private const string Original = "SELECT Id FROM Customers";
    private const string Candidate = "SELECT Id FROM Customers";

    [Fact]
    public async Task ValidationDisabled_ReturnsNotExecuted_AndNeverTouchesTheDatabase()
    {
        var provider = ValidationTestSupport.CreateFixedProvider(new DatabaseComparisonResult
        {
            ResultsEqual = true, OriginalRows = 10, CandidateRows = 10
        });
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = false }, provider);

        var result = await ValidationTestSupport.ValidateAsync(
            validator, Original, Candidate, compareResults: true);

        result.Status.Should().Be(ValidationStatus.NotExecuted);
        result.SemanticallyEquivalent.Should().BeFalse();
        result.ImprovementPercentage.Should().BeNull();
        result.OriginalExecutionTime.Should().BeNull();
        result.OptimizedExecutionTime.Should().BeNull();
        provider.CompareCalls.Should().Be(0, "no database access when runtime validation is disabled");
        result.Limitations.Should().Contain(l => l.Contains("disabled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProviderUnavailable_ReturnsNotExecuted()
    {
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = true });

        var result = await ValidationTestSupport.ValidateAsync(
            validator, Original, Candidate, compareResults: true);

        result.Status.Should().Be(ValidationStatus.NotExecuted);
        result.SemanticallyEquivalent.Should().BeFalse();
        result.ImprovementPercentage.Should().BeNull();
    }

    [Fact]
    public async Task ProviderDialectMismatch_ReturnsNotExecuted()
    {
        var provider = new ValidationTestSupport.FixedComparisonProvider(
            new DatabaseComparisonResult { ResultsEqual = true, OriginalRows = 1, CandidateRows = 1 },
            SqlDialect.MySql);
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = true }, provider);

        var result = await ValidationTestSupport.ValidateAsync(
            validator, Original, Candidate, compareResults: true);

        result.Status.Should().Be(ValidationStatus.NotExecuted);
        result.SemanticallyEquivalent.Should().BeFalse();
        provider.CompareCalls.Should().Be(0);
    }

    [Fact]
    public async Task RuntimeDisabled_StillReportsStaticRiskFindings()
    {
        // NOT IN over a nullable column vs NOT EXISTS: statically inconclusive
        // (NULL semantics). With the runtime stage disabled the status is
        // NotExecuted, but the static risk findings must still be reported.
        var provider = ValidationTestSupport.CreateFixedProvider(new DatabaseComparisonResult
        {
            ResultsEqual = true, OriginalRows = 10, CandidateRows = 10
        });
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = false }, provider);

        var result = await ValidationTestSupport.ValidateAsync(
            validator,
            "SELECT Id FROM Customers c WHERE c.City NOT IN (SELECT City FROM ExcludedCities)",
            "SELECT Id FROM Customers c WHERE NOT EXISTS (SELECT 1 FROM ExcludedCities x WHERE x.City = c.City)",
            compareResults: true);

        result.Status.Should().Be(ValidationStatus.NotExecuted);
        result.SemanticallyEquivalent.Should().BeFalse();
        result.SemanticRisks.Should().NotBeEmpty("static findings are preserved even when the runtime stage did not run");
        provider.CompareCalls.Should().Be(0);
    }

    [Fact]
    public async Task ProviderReturnsNull_Inconclusive_OriginalExecutionFailure()
    {
        // The provider reports an execution failure of either query as null
        // ("no evidence"); the validator must map that to Inconclusive, never
        // to Passed.
        var provider = new ValidationTestSupport.FixedComparisonProvider(null);
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = true }, provider);

        var result = await ValidationTestSupport.ValidateAsync(
            validator, Original, Candidate, compareResults: true);

        result.Status.Should().Be(ValidationStatus.Inconclusive);
        result.SemanticallyEquivalent.Should().BeFalse();
        result.ImprovementPercentage.Should().BeNull();
        provider.CompareCalls.Should().Be(1);
        provider.LastOriginal.Should().Be(Original, "the original query was handed to the provider for execution");
        result.Limitations.Should().Contain(l => l.Contains("could not be executed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProviderReturnsNull_Inconclusive_CandidateExecutionFailure()
    {
        var provider = new ValidationTestSupport.FixedComparisonProvider(null);
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = true }, provider);

        var result = await ValidationTestSupport.ValidateAsync(
            validator, Original, Candidate, compareResults: true);

        result.Status.Should().Be(ValidationStatus.Inconclusive);
        result.SemanticallyEquivalent.Should().BeFalse();
        provider.CompareCalls.Should().Be(1);
        provider.LastCandidate.Should().Be(Candidate, "the candidate query was handed to the provider for execution");
    }

    [Fact]
    public async Task RuntimeMismatch_Failed_ImprovementPercentageNotClaimed()
    {
        // A faster but wrong candidate must never yield an improvement claim.
        var provider = ValidationTestSupport.CreateFixedProvider(new DatabaseComparisonResult
        {
            ResultsEqual = false,
            OriginalRows = 42,
            CandidateRows = 40,
            OriginalDuration = TimeSpan.FromMilliseconds(100),
            CandidateDuration = TimeSpan.FromMilliseconds(10)
        });
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = true }, provider);

        var result = await ValidationTestSupport.ValidateAsync(
            validator, Original, Candidate, compareResults: true);

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SemanticallyEquivalent.Should().BeFalse();
        result.ImprovementPercentage.Should().BeNull(
            "a performance claim requires proven semantic equivalence, which a mismatched result set never has");
    }

    [Fact]
    public async Task TruncatedEqual_Inconclusive_ImprovementPercentageNotClaimed()
    {
        var provider = ValidationTestSupport.CreateFixedProvider(new DatabaseComparisonResult
        {
            ResultsEqual = true,
            OriginalRows = 1000,
            CandidateRows = 1000,
            Truncated = true,
            OriginalDuration = TimeSpan.FromMilliseconds(100),
            CandidateDuration = TimeSpan.FromMilliseconds(10)
        });
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = true }, provider);

        var result = await ValidationTestSupport.ValidateAsync(
            validator, Original, Candidate, compareResults: true, maxRows: 1000);

        result.Status.Should().Be(ValidationStatus.Inconclusive);
        result.SemanticallyEquivalent.Should().BeFalse();
        result.ImprovementPercentage.Should().BeNull(
            "equivalence of the full result sets was not established by a truncated comparison");
    }

    [Fact]
    public async Task CandidateSlower_Passed_NoImprovementClaimed()
    {
        var provider = ValidationTestSupport.CreateFixedProvider(new DatabaseComparisonResult
        {
            ResultsEqual = true,
            OriginalRows = 10,
            CandidateRows = 10,
            OriginalDuration = TimeSpan.FromMilliseconds(80),
            CandidateDuration = TimeSpan.FromMilliseconds(100)
        });
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = true }, provider);

        var result = await ValidationTestSupport.ValidateAsync(
            validator, Original, Candidate, compareResults: true);

        result.Status.Should().Be(ValidationStatus.Passed);
        result.SemanticallyEquivalent.Should().BeTrue();
        result.ImprovementPercentage.Should().BeNull(
            "a slower candidate does not improve performance");
    }

    [Fact]
    public async Task DifferentRowCounts_Failed()
    {
        var provider = ValidationTestSupport.CreateFixedProvider(new DatabaseComparisonResult
        {
            ResultsEqual = false,
            OriginalRows = 5,
            CandidateRows = 3
        });
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = true }, provider);

        var result = await ValidationTestSupport.ValidateAsync(
            validator, Original, Candidate, compareResults: true);

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SemanticallyEquivalent.Should().BeFalse();
        result.Evidence.Should().Contain(e => e.Contains("results differ", StringComparison.OrdinalIgnoreCase));
    }
}