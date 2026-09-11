using FluentAssertions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Services;
using SqlOptimizer.Domain.Analysis;
using Xunit;

namespace SqlOptimizer.Application.Tests.Optimization;

/// <summary>
/// Deterministic ranking: validation status dominates; within a tier the
/// ranking is driven by validation confidence, deterministic rule evidence,
/// capped self-confidence and penalties for warnings/limitations. The LLM
/// confidence can never move a candidate across a status tier, and ties are
/// broken deterministically by candidate id.
/// </summary>
public class CandidateRankerTests
{
    private readonly CandidateRanker _ranker = new();

    [Fact]
    public void ValidatedBeatsInconclusive_WhateverTheConfidence()
    {
        var analysis = AnalysisWithFinding();
        var validated = Candidate("A", CandidateStatus.Validated, confidence: 0.1,
            validation: new ValidationResult { Status = ValidationStatus.Passed, ValidationConfidence = 0.85 });
        var inconclusive = Candidate("B", CandidateStatus.Inconclusive, confidence: 0.99,
            validation: new ValidationResult { Status = ValidationStatus.Inconclusive, ValidationConfidence = 0.5 });

        var ranked = _ranker.Rank([validated, inconclusive], analysis);

        ranked.Select(c => c.CandidateId).Should().Equal("A", "B");
        ranked[0].Rank.Should().Be(1);
        ranked[1].Rank.Should().Be(2);
    }

    [Fact]
    public void Rejected_RankedLast()
    {
        var analysis = AnalysisWithFinding();
        var inconclusive = Candidate("A", CandidateStatus.Inconclusive, confidence: 0.9);
        var rejected = Candidate("B", CandidateStatus.Rejected, confidence: 0.9);

        var ranked = _ranker.Rank([rejected, inconclusive], analysis);

        ranked.Select(c => c.CandidateId).Should().Equal("A", "B");
    }

    [Fact]
    public void WithinSameTier_HigherValidationConfidenceWins()
    {
        var analysis = AnalysisWithFinding();
        var low = Candidate("A", CandidateStatus.Inconclusive, confidence: 0.9,
            validation: new ValidationResult { Status = ValidationStatus.Inconclusive, ValidationConfidence = 0.3 });
        var high = Candidate("B", CandidateStatus.Inconclusive, confidence: 0.2,
            validation: new ValidationResult { Status = ValidationStatus.Inconclusive, ValidationConfidence = 0.9 });

        var ranked = _ranker.Rank([low, high], analysis);

        ranked.Select(c => c.CandidateId).Should().Equal("B", "A");
    }

    [Fact]
    public void RuleEvidence_RewardsCandidatesAddressingFindings()
    {
        var analysis = AnalysisWithFinding();
        var addressing = Candidate("A", CandidateStatus.Inconclusive, confidence: 0.5, ruleIds: ["SQL001"]);
        var unrelated = Candidate("B", CandidateStatus.Inconclusive, confidence: 0.5, ruleIds: ["SQL999"]);

        var ranked = _ranker.Rank([unrelated, addressing], analysis);

        ranked.Select(c => c.CandidateId).Should().Equal("A", "B");
    }

    [Fact]
    public void HighLlmConfidence_CannotOverrideSameStatusTieBreak()
    {
        // Two inconclusive candidates with identical evidence: the one with
        // fewer limitations wins, even with a much lower self confidence.
        var analysis = AnalysisWithFinding();
        var clean = Candidate("A", CandidateStatus.Inconclusive, confidence: 0.3,
            validation: new ValidationResult { Status = ValidationStatus.Inconclusive, ValidationConfidence = 0.8 });
        var noisy = Candidate("B", CandidateStatus.Inconclusive, confidence: 0.99,
            validation: new ValidationResult { Status = ValidationStatus.Inconclusive, ValidationConfidence = 0.8 },
            limitations: ["warning 1", "warning 2", "warning 3"]);

        var ranked = _ranker.Rank([noisy, clean], analysis);

        ranked.Select(c => c.CandidateId).Should().Equal("A", "B");
    }

    [Fact]
    public void OrderIsStable_ForIdenticalCandidates()
    {
        var analysis = AnalysisWithFinding();
        var first = Candidate("B", CandidateStatus.Inconclusive, confidence: 0.5);
        var second = Candidate("A", CandidateStatus.Inconclusive, confidence: 0.5);

        var ranked = _ranker.Rank([first, second], analysis);

        ranked.Select(c => c.CandidateId).Should().Equal("A", "B");
    }

    private static SqlAnalysis AnalysisWithFinding() => new()
    {
        Sql = "SELECT * FROM Customers",
        Dialect = Domain.Common.SqlDialect.SqlServer,
        Ast = null!,
        ComplexityScore = 10,
        PerformanceScore = 5,
        Findings = [new SqlFinding
        {
            RuleId = "SQL001",
            Severity = Severity.Warning,
            Category = FindingCategory.Maintainability,
            Message = "star",
            Confidence = 0.9,
            Impact = new OptimizationImpact(2, 1, 5, 1)
        }],
        Statistics = new QueryStatistics()
    };

    private static OptimizationCandidate Candidate(
        string id,
        CandidateStatus status,
        double confidence,
        ValidationResult? validation = null,
        IReadOnlyList<string>? ruleIds = null,
        IReadOnlyList<string>? limitations = null) => new(
        CandidateId: id,
        OriginalSql: "SELECT 1",
        CandidateSql: $"SELECT 2 -- {id}",
        Source: status == CandidateStatus.Validated ? CandidateSource.Rule : CandidateSource.Llm,
        RuleIds: ruleIds ?? [],
        Explanation: "test",
        ExpectedOptimization: null,
        Confidence: confidence,
        Warnings: [],
        Assumptions: [],
        Limitations: limitations ?? [],
        Status: status,
        Validation: validation);
}
