using FluentAssertions;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Application.Services;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Infrastructure.Parsing;
using Xunit;

namespace SqlOptimizer.Application.Tests.Optimization;

/// <summary>
/// LLM candidate generation: structured evidence goes in, defensively parsed
/// candidates come out (status Generated, never Validated). Every failure
/// mode degrades to an empty result with an explicit limitation — the
/// pipeline never crashes and never fabricates LLM candidates.
/// </summary>
public class LlmCandidateGeneratorTests
{
    [Fact]
    public async Task UseLlmDisabled_NoLlmCall()
    {
        var (generator, llm) = CreateGenerator(new LlmOptions(Provider: "Mock"));
        var context = BuildContext("SELECT * FROM Customers", new OptimizationOptions { UseLlm = false });

        var result = await generator.GenerateAsync(context);

        result.Candidates.Should().BeEmpty();
        llm.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task NoProviderConfigured_ReturnsLimitation_NoLlmCall()
    {
        var (generator, llm) = CreateGenerator(new LlmOptions(Provider: ""));
        var context = BuildContext("SELECT * FROM Customers", new OptimizationOptions());

        var result = await generator.GenerateAsync(context);

        result.Candidates.Should().BeEmpty();
        result.Limitations.Should().ContainSingle().Which.Should().Contain("not configured");
        llm.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ValidLlmResponse_ProducesGeneratedCandidates()
    {
        var response = new LlmResponse(
            """
            {"candidates":[
              {"sql":"SELECT Id, Name FROM Customers WHERE Id > 10",
               "explanation":"narrows the projection",
               "expectedImpact":"less I/O",
               "confidence":0.8,
               "warnings":["assumes Id>10 is intended"],
               "assumptions":["no extra columns needed"]}
            ]}
            """,
            Model: "test-model",
            FinishReason: "stop");
        var (generator, llm) = CreateGenerator(new LlmOptions(Provider: "Mock"), response);
        var context = BuildContext("SELECT Id, Name, City FROM Customers WHERE Id > 10", new OptimizationOptions());

        var result = await generator.GenerateAsync(context);

        llm.CallCount.Should().Be(1);
        result.Candidates.Should().HaveCount(1);
        var candidate = result.Candidates[0];
        candidate.Source.Should().Be(CandidateSource.Llm);
        candidate.Status.Should().Be(CandidateStatus.Generated);
        candidate.Explanation.Should().Be("narrows the projection");
        candidate.ExpectedOptimization.Should().Be("less I/O");
        candidate.Confidence.Should().Be(0.8);
        candidate.Warnings.Should().Contain("assumes Id>10 is intended");
        candidate.Assumptions.Should().Contain("no extra columns needed");
        candidate.Limitations.Should().Contain(l => l.Contains("validation", StringComparison.OrdinalIgnoreCase));
        candidate.Validation.Should().BeNull();
    }

    [Fact]
    public async Task MalformedLlmResponse_ReturnsLimitation_NoCandidates()
    {
        var (generator, _) = CreateGenerator(
            new LlmOptions(Provider: "Mock"),
            new LlmResponse("I cannot produce JSON."));

        var result = await generator.GenerateAsync(BuildContext("SELECT * FROM Customers", new OptimizationOptions()));

        result.Candidates.Should().BeEmpty();
        result.Limitations.Should().ContainSingle().Which.Should().Contain("could not be parsed");
    }

    [Fact]
    public async Task ProviderException_ReturnsLimitation_NoCandidates()
    {
        var failing = new OptimizationTestSupport.FakeLlmProvider
        {
            ThrowOnCall = true,
            Exception = new LlmException("provider down")
        };
        var generator = CreateGeneratorWith(new LlmOptions(Provider: "Mock"), failing).generator;

        var result = await generator.GenerateAsync(BuildContext("SELECT * FROM Customers", new OptimizationOptions()));

        result.Candidates.Should().BeEmpty();
        result.Limitations.Should().ContainSingle().Which.Should().Contain("provider down");
    }

    [Fact]
    public async Task NotConfiguredException_ReturnsLimitation_NoCandidates()
    {
        var failing = new OptimizationTestSupport.FakeLlmProvider
        {
            ThrowOnCall = true,
            Exception = new LlmNotConfiguredException("missing endpoint")
        };
        var generator = CreateGeneratorWith(new LlmOptions(Provider: "Mock"), failing).generator;

        var result = await generator.GenerateAsync(BuildContext("SELECT * FROM Customers", new OptimizationOptions()));

        result.Candidates.Should().BeEmpty();
        result.Limitations.Should().ContainSingle().Which.Should().Contain("not configured");
    }

    [Fact]
    public async Task ExcessiveCandidates_AreTruncatedAndReported()
    {
        var items = string.Join(",", Enumerable.Range(1, 5).Select(i => $"{{\"sql\":\"SELECT {i} AS C\",\"confidence\":0.5}}"));
        var (generator, _) = CreateGenerator(
            new LlmOptions(Provider: "Mock"),
            new LlmResponse($"{{\"candidates\":[{items}]}}"));
        var context = BuildContext("SELECT 1", new OptimizationOptions { MaxCandidates = 2 });

        var result = await generator.GenerateAsync(context);

        result.Candidates.Should().HaveCount(2);
        result.Limitations.Should().Contain(l => l.Contains("maximum of 2"));
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var (generator, _) = CreateGenerator(
            new LlmOptions(Provider: "Mock"),
            new LlmResponse("{\"candidates\":[]}"),
            delayMs: 500);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        var act = () => generator.GenerateAsync(BuildContext("SELECT 1", new OptimizationOptions()), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>Creates the generator with the given options and queued LLM responses.</summary>
    private static (LlmCandidateGenerator, OptimizationTestSupport.FakeLlmProvider) CreateGenerator(
        LlmOptions options,
        LlmResponse? response = null,
        int delayMs = 0)
    {
        var llm = new OptimizationTestSupport.FakeLlmProvider(response!) { DelayMs = delayMs };
        return CreateGeneratorWith(options, llm);
    }

    /// <summary>Creates the generator around a specific fake client.</summary>
    private static (LlmCandidateGenerator generator, OptimizationTestSupport.FakeLlmProvider llm) CreateGeneratorWith(
        LlmOptions options,
        OptimizationTestSupport.FakeLlmProvider llm)
    {
        var generator = new LlmCandidateGenerator(
            llm,
            new PromptGenerator(),
            new LlmResponseParser(),
            options,
            new SqlOptimizerOptions());
        return (generator, llm);
    }

    /// <summary>Parses the query, runs the real rules and builds the context DTO.</summary>
    private static SqlOptimizationContext BuildContext(string sql, OptimizationOptions options)
    {
        var parser = new SqlServerSqlParser();
        var parsed = parser.Parse(sql);
        var findings = OptimizationTestSupport.CreateRuleRegistry().Analyze(new SqlAnalysisContext
        {
            Ast = parsed.Root,
            Dialect = SqlDialect.SqlServer
        });

        var analysis = new SqlAnalysis
        {
            Sql = sql,
            Dialect = SqlDialect.SqlServer,
            Ast = parsed.Root,
            ComplexityScore = 10,
            PerformanceScore = 5,
            Findings = findings,
            Statistics = QueryStatistics.FromAst(parsed.Root)
        };

        return new SqlOptimizationContext(
            sql,
            SqlDialect.SqlServer,
            analysis,
            null,
            null,
            options,
            new Domain.Optimization.OptimizationPlan([]),
            []);
    }
}
