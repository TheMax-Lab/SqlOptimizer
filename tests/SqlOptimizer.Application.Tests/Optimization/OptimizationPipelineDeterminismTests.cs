using FluentAssertions;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Options;
using Xunit;

namespace SqlOptimizer.Application.Tests.Optimization;

/// <summary>
/// M12 determinism regression: the same SQL + same options must always
/// produce the same analysis, the same plan, the same recommendations and
/// the same deterministic candidate output. This pins the project-wide
/// determinism requirement end to end, including the LLM path (with the
/// deterministic mock client) where the LLM response is fixed.
/// </summary>
public class OptimizationPipelineDeterminismTests
{
    private const string LlmJson =
        """
        {"candidates":[{"sql":"SELECT Id FROM Customers","confidence":0.9}]}
        """;

    [Fact]
    public async Task SameRequestTwice_ProducesIdenticalEndToEndResults()
    {
        // One queued (fixed) LLM response per run, so both runs see the same LLM output.
        var (optimizer, llm, _) = OptimizationTestSupport.CreatePipeline(
            new LlmOptions(Provider: "Mock"),
            llm: new OptimizationTestSupport.FakeLlmProvider(new LlmResponse(LlmJson), new LlmResponse(LlmJson)));

        var request1 = OptimizationTestSupport.Request(
            "SELECT * FROM Customers", new OptimizationOptions(), OptimizationTestSupport.TestSchema());
        var request2 = OptimizationTestSupport.Request(
            "SELECT * FROM Customers", new OptimizationOptions(), OptimizationTestSupport.TestSchema());

        var first = await optimizer.OptimizeAsync(request1);
        var second = await optimizer.OptimizeAsync(request2);

        llm.CallCount.Should().Be(2);

        // Same analysis.
        first.Analysis.ComplexityScore.Should().Be(second.Analysis.ComplexityScore);
        first.Analysis.PerformanceScore.Should().Be(second.Analysis.PerformanceScore);
        first.Analysis.Findings
            .Select(f => (f.RuleId, f.Message, f.Severity, f.Confidence))
            .Should().BeEquivalentTo(
                second.Analysis.Findings.Select(f => (f.RuleId, f.Message, f.Severity, f.Confidence)));

        // Same plan.
        first.OptimizationPlan.EstimatedTotalBenefit.Should().Be(second.OptimizationPlan.EstimatedTotalBenefit);
        first.OptimizationPlan.Actions
            .Select(a => (a.RelatedRuleId, a.Risk, a.GetType().Name))
            .Should().BeEquivalentTo(
                second.OptimizationPlan.Actions.Select(a => (a.RelatedRuleId, a.Risk, a.GetType().Name)));

        // Same deterministic candidate output: SQL, source, status, rank, confidence.
        first.Candidates.Should().HaveSameCount(second.Candidates);
        for (var i = 0; i < first.Candidates.Count; i++)
        {
            var a = first.Candidates[i];
            var b = second.Candidates[i];

            a.CandidateSql.Should().Be(b.CandidateSql);
            a.Source.Should().Be(b.Source);
            a.Status.Should().Be(b.Status);
            a.Rank.Should().Be(b.Rank);
            a.Confidence.Should().Be(b.Confidence);
            a.Validation!.Status.Should().Be(b.Validation!.Status);
        }

        // Same limitations (order-insensitive but same set).
        first.Limitations.Should().BeEquivalentTo(second.Limitations);
    }

    [Fact]
    public async Task DeterministicOnlyPath_IsStableAcrossRepeats()
    {
        // No LLM at all: pure deterministic generation/validation/ranking.
        var (optimizer, _, _) = OptimizationTestSupport.CreatePipeline(
            new LlmOptions(Provider: ""));

        SqlOptimizationResult? first = null;
        SqlOptimizationResult? second = null;

        for (var run = 0; run < 2; run++)
        {
            var result = await optimizer.OptimizeAsync(OptimizationTestSupport.Request(
                "SELECT * FROM Customers WHERE LOWER(Name) = 'rome'",
                new OptimizationOptions { UseLlm = false },
                OptimizationTestSupport.TestSchema()));

            if (run == 0)
            {
                first = result;
            }
            else
            {
                second = result;
            }
        }

        first!.Candidates.Select(c => (c.CandidateSql, c.Status, c.Rank, c.Validation!.Status))
            .Should().BeEquivalentTo(
                second!.Candidates.Select(c => (c.CandidateSql, c.Status, c.Rank, c.Validation!.Status)));
    }
}
