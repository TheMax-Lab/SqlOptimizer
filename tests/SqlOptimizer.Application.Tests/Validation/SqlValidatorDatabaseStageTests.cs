using FluentAssertions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Options;
using Xunit;

namespace SqlOptimizer.Application.Tests.Validation;

/// <summary>
/// Stage D of validation: the optional database-backed result comparison.
/// The runtime stage is gated by the request flag, the server switch and
/// provider availability; <c>SemanticallyEquivalent</c> is only ever true
/// when a real comparison ran and proved equality. A requested comparison
/// that could not be attempted at all is reported as
/// <c>NotExecuted</c>, never as <c>Passed</c>.
/// </summary>
public class SqlValidatorDatabaseStageTests
{
    [Fact]
    public async Task RuntimeDisabled_ProviderNotInvoked()
    {
        var provider = ValidationTestSupport.CreateFixedProvider(new DatabaseComparisonResult
        {
            ResultsEqual = true, OriginalRows = 10, CandidateRows = 10
        });
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = false }, provider);

        var result = await ValidationTestSupport.ValidateAsync(
            validator, "SELECT Id FROM Customers", "SELECT Id FROM Customers", compareResults: true);

        provider.CompareCalls.Should().Be(0);
        result.Status.Should().Be(ValidationStatus.NotExecuted);
        result.SemanticallyEquivalent.Should().BeFalse();
        result.ImprovementPercentage.Should().BeNull();
        result.Limitations.Should().Contain(l => l.Contains("disabled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProviderUnavailable_AddsLimitation()
    {
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = true });

        var result = await ValidationTestSupport.ValidateAsync(
            validator, "SELECT Id FROM Customers", "SELECT Id FROM Customers", compareResults: true);

        result.Status.Should().Be(ValidationStatus.NotExecuted);
        result.SemanticallyEquivalent.Should().BeFalse();
        result.Limitations.Should().Contain(l => l.Contains("no database validation provider", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RuntimeEqual_Passes_WithSemanticEquivalence()
    {
        var provider = ValidationTestSupport.CreateFixedProvider(new DatabaseComparisonResult
        {
            ResultsEqual = true,
            OriginalRows = 42,
            CandidateRows = 42,
            OriginalDuration = TimeSpan.FromMilliseconds(100),
            CandidateDuration = TimeSpan.FromMilliseconds(80)
        });
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = true }, provider);

        var result = await ValidationTestSupport.ValidateAsync(
            validator, "SELECT Id FROM Customers", "SELECT Id FROM Customers", compareResults: true);

        provider.CompareCalls.Should().Be(1);
        result.Status.Should().Be(ValidationStatus.Passed);
        result.SemanticallyEquivalent.Should().BeTrue();
        result.OriginalExecutionTime.Should().Be(TimeSpan.FromMilliseconds(100));
        result.OptimizedExecutionTime.Should().Be(TimeSpan.FromMilliseconds(80));
        result.ImprovementPercentage.Should().Be(20.0);
    }

    [Fact]
    public async Task RuntimeDifferent_Fails()
    {
        var provider = ValidationTestSupport.CreateFixedProvider(new DatabaseComparisonResult
        {
            ResultsEqual = false, OriginalRows = 42, CandidateRows = 40
        });
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = true }, provider);

        var result = await ValidationTestSupport.ValidateAsync(
            validator, "SELECT Id FROM Customers", "SELECT Id FROM Customers", compareResults: true);

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SemanticallyEquivalent.Should().BeFalse();
        result.Evidence.Should().Contain(e => e.Contains("results differ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RuntimeEqual_ButProvableStructureChange_StillFails()
    {
        var provider = ValidationTestSupport.CreateFixedProvider(new DatabaseComparisonResult
        {
            ResultsEqual = true, OriginalRows = 10, CandidateRows = 10
        });
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = true }, provider);

        var result = await ValidationTestSupport.ValidateAsync(
            validator, "SELECT Id FROM Customers", "SELECT Id FROM Orders", compareResults: true);

        result.Status.Should().Be(ValidationStatus.Failed);
        result.SemanticallyEquivalent.Should().BeFalse();
    }

    [Fact]
    public async Task ComparisonCapIsPassedToProvider()
    {
        var provider = ValidationTestSupport.CreateFixedProvider(new DatabaseComparisonResult
        {
            ResultsEqual = true, OriginalRows = 5, CandidateRows = 5
        });
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = true }, provider);

        await ValidationTestSupport.ValidateAsync(
            validator, "SELECT Id FROM Customers", "SELECT Id FROM Customers", compareResults: true, maxRows: 100);

        provider.LastMaxRows.Should().Be(100);
        provider.LastOriginal.Should().Be("SELECT Id FROM Customers");
        provider.LastCandidate.Should().Be("SELECT Id FROM Customers");
    }

    [Fact]
    public async Task InconclusiveStaticResult_StaysInconclusive_WithEqualRuntimeResults()
    {
        var provider = ValidationTestSupport.CreateFixedProvider(new DatabaseComparisonResult
        {
            ResultsEqual = true, OriginalRows = 100, CandidateRows = 100, Truncated = true
        });
        var validator = ValidationTestSupport.CreateValidator(
            new SqlOptimizerOptions { EnableRuntimeValidation = true }, provider);

        var result = await ValidationTestSupport.ValidateAsync(
            validator,
            "SELECT Id FROM Customers c WHERE c.City NOT IN (SELECT City FROM ExcludedCities)",
            "SELECT Id FROM Customers c WHERE NOT EXISTS (SELECT 1 FROM ExcludedCities x WHERE x.City = c.City)",
            compareResults: true);

        result.Status.Should().Be(ValidationStatus.Inconclusive);
        result.SemanticallyEquivalent.Should().BeFalse();
        result.Limitations.Should().Contain(l => l.Contains("truncated", StringComparison.OrdinalIgnoreCase));
    }
}
