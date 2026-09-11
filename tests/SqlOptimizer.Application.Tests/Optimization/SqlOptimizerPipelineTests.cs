using FluentAssertions;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Application.Services;
using Xunit;

namespace SqlOptimizer.Application.Tests.Optimization;

/// <summary>
/// End-to-end pipeline behavior: analyze → generate → deduplicate →
/// validate (always, for every candidate) → rank. LLM candidates can never
/// be presented as validated, Inconclusive is never reinterpreted as
/// success, and every failure mode degrades gracefully.
/// </summary>
public class SqlOptimizerPipelineTests
{
    private const string LlmProvider = "Mock";

    [Fact]
    public async Task CleanQuery_NoCandidates_NullValidation()
    {
        var (optimizer, llm, _) = OptimizationTestSupport.CreatePipeline(
            new LlmOptions(Provider: ""));

        var result = await optimizer.OptimizeAsync(
            OptimizationTestSupport.Request("SELECT Id FROM Customers WHERE Id = 10", new OptimizationOptions()));

        result.Candidates.Should().BeEmpty();
        result.Validation.Should().BeNull();
        llm.CallCount.Should().Be(0);
        result.Analysis.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task StarWithSchema_DeterministicCandidateValidated()
    {
        var (optimizer, _, validator) = OptimizationTestSupport.CreatePipeline(
            new LlmOptions(Provider: ""));

        var result = await optimizer.OptimizeAsync(
            OptimizationTestSupport.Request(
                "SELECT * FROM Customers",
                new OptimizationOptions(),
                OptimizationTestSupport.TestSchema()));

        result.Candidates.Should().HaveCount(1);
        var candidate = result.Candidates[0];
        candidate.CandidateSql.Should().Be("SELECT Id, Name, City FROM Customers");
        candidate.Source.Should().Be(CandidateSource.Rule);
        candidate.Status.Should().Be(CandidateStatus.Validated);
        candidate.Validation.Should().NotBeNull();
        candidate.Validation!.Status.Should().Be(ValidationStatus.Passed);
        candidate.Rank.Should().Be(1);
        validator.CallCount.Should().Be(1);
        result.Validation.Should().BeSameAs(candidate.Validation);
    }

    [Fact]
    public async Task StarWithoutSchema_NoDeterministicCandidate()
    {
        var (optimizer, _, validator) = OptimizationTestSupport.CreatePipeline(
            new LlmOptions(Provider: ""));

        var result = await optimizer.OptimizeAsync(
            OptimizationTestSupport.Request("SELECT * FROM Customers", new OptimizationOptions()));

        result.Candidates.Should().BeEmpty();
        validator.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task LlmCandidateWithDifferentTables_Rejected()
    {
        var (optimizer, llm, _) = CreateLlmPipeline(new LlmResponse(
            """
            {"candidates":[{"sql":"SELECT OrderId FROM Orders","confidence":0.9}]}
            """));

        var result = await optimizer.OptimizeAsync(
            OptimizationTestSupport.Request("SELECT Id FROM Customers", new OptimizationOptions()));

        llm.CallCount.Should().Be(1);
        result.Candidates.Should().HaveCount(1);
        var candidate = result.Candidates[0];
        candidate.Source.Should().Be(CandidateSource.Llm);
        candidate.Status.Should().Be(CandidateStatus.Rejected);
        candidate.Validation!.Status.Should().Be(ValidationStatus.Failed);
        candidate.Validation.SemanticallyEquivalent.Should().BeFalse();
    }

    [Fact]
    public async Task LlmCandidateInconclusive_NeverValidated()
    {
        var (optimizer, llm, _) = CreateLlmPipeline(new LlmResponse(
            """
            {"candidates":[{"sql":"SELECT Id FROM Customers WHERE Id >= 10","confidence":0.99}]}
            """));

        var result = await optimizer.OptimizeAsync(
            OptimizationTestSupport.Request(
                "SELECT Id FROM Customers WHERE Id > 10", new OptimizationOptions()));

        llm.CallCount.Should().Be(1);
        result.Candidates.Should().HaveCount(1);
        var candidate = result.Candidates[0];
        candidate.Confidence.Should().Be(0.99);
        candidate.Status.Should().Be(CandidateStatus.Inconclusive);
        candidate.Validation!.Status.Should().Be(ValidationStatus.Inconclusive);
        candidate.Validation.SemanticallyEquivalent.Should().BeFalse();
    }

    [Fact]
    public async Task ValidatedCandidate_OutranksHighConfidenceInconclusive()
    {
        var (optimizer, llm, _) = OptimizationTestSupport.CreatePipeline(
            new LlmOptions(Provider: LlmProvider),
            llm: new OptimizationTestSupport.FakeLlmProvider(new LlmResponse(
                """
                {"candidates":[{"sql":"SELECT * FROM Customers WHERE Id > 10","confidence":0.99}]}
                """)));

        var result = await optimizer.OptimizeAsync(
            OptimizationTestSupport.Request(
                "SELECT * FROM Customers",
                new OptimizationOptions(),
                OptimizationTestSupport.TestSchema()));

        llm.CallCount.Should().Be(1);
        result.Candidates.Should().HaveCount(2);
        result.Candidates[0].Source.Should().Be(CandidateSource.Rule);
        result.Candidates[0].Status.Should().Be(CandidateStatus.Validated);
        result.Candidates[0].Rank.Should().Be(1);
        result.Candidates[1].Status.Should().Be(CandidateStatus.Inconclusive);
        result.Candidates[1].Confidence.Should().Be(0.99);
    }

    [Fact]
    public async Task Validation_AlwaysInvoked_ForEveryCandidate()
    {
        var (optimizer, llm, validator) = OptimizationTestSupport.CreatePipeline(
            new LlmOptions(Provider: LlmProvider),
            llm: new OptimizationTestSupport.FakeLlmProvider(new LlmResponse(
                """
                {"candidates":[
                  {"sql":"SELECT Id, Name FROM Customers WHERE Id > 10","confidence":0.7},
                  {"sql":"SELECT Id FROM Customers WHERE Id > 10","confidence":0.6}
                ]}
                """)));

        var result = await optimizer.OptimizeAsync(
            OptimizationTestSupport.Request(
                "SELECT Id, Name, City FROM Customers WHERE Id > 10", new OptimizationOptions()));

        validator.CallCount.Should().Be(2, "every generated candidate is validated");
        result.Candidates.Should().HaveCount(2);
        result.Candidates.Should().OnlyContain(c => c.Validation != null);
    }

    [Fact]
    public async Task DuplicateCandidates_AreDeduplicated()
    {
        // The LLM returns exactly the deterministic star expansion: one
        // candidate remains, reported as a discarded duplicate.
        var (optimizer, llm, validator) = OptimizationTestSupport.CreatePipeline(
            new LlmOptions(Provider: LlmProvider),
            llm: new OptimizationTestSupport.FakeLlmProvider(new LlmResponse(
                """
                {"candidates":[{"sql":"SELECT Id, Name, City FROM Customers","confidence":0.5}]}
                """)));

        var result = await optimizer.OptimizeAsync(
            OptimizationTestSupport.Request(
                "SELECT * FROM Customers",
                new OptimizationOptions(),
                OptimizationTestSupport.TestSchema()));

        llm.CallCount.Should().Be(1);
        result.Candidates.Should().HaveCount(1);
        result.Candidates[0].Source.Should().Be(CandidateSource.Rule, "the deterministic candidate is kept");
        validator.CallCount.Should().Be(1);
        result.Limitations.Should().Contain(l => l.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LlmUnavailable_DegradesGracefully()
    {
        var (optimizer, _, validator) = OptimizationTestSupport.CreatePipeline(
            new LlmOptions(Provider: ""));

        var result = await optimizer.OptimizeAsync(
            OptimizationTestSupport.Request(
                "SELECT * FROM Customers",
                new OptimizationOptions(),
                OptimizationTestSupport.TestSchema()));

        result.Candidates.Should().HaveCount(1, "the deterministic candidate is still produced");
        result.Candidates[0].Status.Should().Be(CandidateStatus.Validated);
        result.Limitations.Should().Contain(l => l.Contains("not configured", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LlmProviderFails_DegradesGracefully()
    {
        var failing = new OptimizationTestSupport.FakeLlmProvider
        {
            ThrowOnCall = true,
            Exception = new Domain.Common.LlmException("connection reset")
        };
        var (optimizer, _, validator) = OptimizationTestSupport.CreatePipeline(
            new LlmOptions(Provider: LlmProvider),
            llm: failing);

        var result = await optimizer.OptimizeAsync(
            OptimizationTestSupport.Request(
                "SELECT * FROM Customers",
                new OptimizationOptions(),
                OptimizationTestSupport.TestSchema()));

        result.Candidates.Should().HaveCount(1, "the deterministic candidate is still produced and validated");
        validator.CallCount.Should().Be(1);
        result.Limitations.Should().Contain(l => l.Contains("connection reset"));
    }

    [Fact]
    public async Task ValidatorThrows_CandidateInconclusive_PipelineSucceeds()
    {
        var (optimizer, _, validator) = OptimizationTestSupport.CreatePipeline(
            new LlmOptions(Provider: ""));
        validator.ThrowOnCall = true;

        var result = await optimizer.OptimizeAsync(
            OptimizationTestSupport.Request(
                "SELECT * FROM Customers",
                new OptimizationOptions(),
                OptimizationTestSupport.TestSchema()));

        result.Candidates.Should().HaveCount(1);
        var candidate = result.Candidates[0];
        candidate.Status.Should().Be(CandidateStatus.Inconclusive, "a failed validation run is never a success");
        candidate.Validation!.Status.Should().Be(ValidationStatus.NotExecuted);
        candidate.Validation.Errors.Should().Contain("Simulated validator failure.");
        result.Limitations.Should().Contain(l => l.Contains("could not be executed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UseLlmFalse_LlmNeverCalled()
    {
        var (optimizer, llm, _) = OptimizationTestSupport.CreatePipeline(
            new LlmOptions(Provider: LlmProvider));

        var result = await optimizer.OptimizeAsync(
            OptimizationTestSupport.Request("SELECT * FROM Customers", new OptimizationOptions { UseLlm = false }));

        llm.CallCount.Should().Be(0);
        result.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateIndexesFalse_NoIndexRecommendations()
    {
        var (optimizer, _, _) = OptimizationTestSupport.CreatePipeline(
            new LlmOptions(Provider: ""));

        var result = await optimizer.OptimizeAsync(
            OptimizationTestSupport.Request(
                "SELECT Id FROM Customers WHERE Id = 10",
                new OptimizationOptions { GenerateIndexes = false }));

        result.Indexes.Should().BeEmpty();
    }

    [Fact]
    public async Task Prompt_ExposedWhenRequested_AndOmittedOtherwise()
    {
        var (optimizerWithPrompt, _, _) = OptimizationTestSupport.CreatePipeline(
            new LlmOptions(Provider: ""));

        var withPrompt = await optimizerWithPrompt.OptimizeAsync(
            OptimizationTestSupport.Request(
                "SELECT * FROM Customers", new OptimizationOptions { GeneratePrompt = true }));

        withPrompt.Prompt.Should().NotBeNull();
        withPrompt.Prompt!.SystemPrompt.Should().NotBeNullOrWhiteSpace();
        withPrompt.Prompt.UserPrompt.Should().Contain("SELECT * FROM Customers");

        var withoutPrompt = await optimizerWithPrompt.OptimizeAsync(
            OptimizationTestSupport.Request(
                "SELECT * FROM Customers", new OptimizationOptions { UseLlm = false, GeneratePrompt = false }));

        withoutPrompt.Prompt.Should().BeNull();
    }

    [Fact]
    public async Task CancelledRequest_ThrowsOperationCanceled()
    {
        var (optimizer, _, _) = OptimizationTestSupport.CreatePipeline(
            new LlmOptions(Provider: ""));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => optimizer.OptimizeAsync(
            OptimizationTestSupport.Request("SELECT 1", new OptimizationOptions()), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task EmptyCandidateList_EmptyResult()
    {
        var (optimizer, _, _) = OptimizationTestSupport.CreatePipeline(
            new LlmOptions(Provider: ""));

        var result = await optimizer.OptimizeAsync(
            OptimizationTestSupport.Request(
                "SELECT Id FROM Customers WHERE Id = 10", new OptimizationOptions { UseLlm = false }));

        result.Candidates.Should().BeEmpty();
        result.Validation.Should().BeNull();
        result.OptimizationPlan.Actions.Should().BeEmpty();
    }

    /// <summary>Creates a pipeline with a configured LLM provider and queued responses.</summary>
    private static (SqlOptimizationService Optimizer, OptimizationTestSupport.FakeLlmProvider Llm,
        OptimizationTestSupport.CountingValidator Validator) CreateLlmPipeline(params LlmResponse[] responses) =>
        OptimizationTestSupport.CreatePipeline(
            new LlmOptions(Provider: LlmProvider),
            llm: new OptimizationTestSupport.FakeLlmProvider(responses));
}
