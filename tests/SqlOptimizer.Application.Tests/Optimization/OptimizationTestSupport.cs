using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Application.Services;
using SqlOptimizer.Application.Services.Validation;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Infrastructure.Parsing;
using SqlOptimizer.Rules;
using SqlOptimizer.Rules.Scoring;

namespace SqlOptimizer.Application.Tests.Optimization;

/// <summary>
/// Shared factory for optimization pipeline tests: production parser,
/// production rules, production validator and production generators, with
/// only the external LLM client and (optionally) the validator wrapped in
/// test doubles. No mocks of the analyzer, the rules or the plan builder.
/// </summary>
internal static class OptimizationTestSupport
{
    /// <summary>Schema used by the tests: Customers (Id, Name, City) and Orders.</summary>
    internal static DatabaseSchema TestSchema() => new(new[]
    {
        new DatabaseTable("dbo", "Customers", 10_000, new[]
        {
            new DatabaseColumn("Id", "INT", Nullable: false, PrimaryKey: true),
            new DatabaseColumn("Name", "NVARCHAR(100)", Nullable: true, PrimaryKey: false),
            new DatabaseColumn("City", "NVARCHAR(50)", Nullable: true, PrimaryKey: false)
        }, Array.Empty<DatabaseIndex>()),
        new DatabaseTable("dbo", "Orders", 50_000, new[]
        {
            new DatabaseColumn("OrderId", "INT", Nullable: false, PrimaryKey: true),
            new DatabaseColumn("CustomerId", "INT", Nullable: false, PrimaryKey: false),
            new DatabaseColumn("Total", "DECIMAL(18,2)", Nullable: true, PrimaryKey: false)
        }, Array.Empty<DatabaseIndex>())
    });

    /// <summary>Creates a full pipeline with the given LLM options and client.</summary>
    internal static (SqlOptimizationService Optimizer, FakeLlmProvider Llm, CountingValidator Validator) CreatePipeline(
        LlmOptions? llmOptions = null,
        SqlOptimizerOptions? sqlOptions = null,
        FakeLlmProvider? llm = null)
    {
        var options = sqlOptions ?? new SqlOptimizerOptions();
        var validator = new CountingValidator();
        var fakeLlm = llm ?? new FakeLlmProvider();

        var parser = new SqlServerSqlParser();
        var registry = CreateRuleRegistry();
        var analyzer = new SqlAnalyzer(parser, registry, new DeterministicScoreEngine(), options);

        var generators = new List<ISqlOptimizationCandidateGenerator>
        {
            new DeterministicCandidateGenerator(),
            new LlmCandidateGenerator(
                fakeLlm,
                new PromptGenerator(),
                new LlmResponseParser(),
                llmOptions ?? new LlmOptions(),
                options)
        };

        var optimizer = new SqlOptimizationService(
            analyzer,
            new OptimizationPlanBuilder(),
            new IndexAdvisor(),
            generators,
            validator,
            new CandidateRanker(),
            new PromptGenerator(),
            options);

        return (optimizer, fakeLlm, validator);
    }

    /// <summary>Creates the production registry with all 20 deterministic rules.</summary>
    internal static RuleRegistry CreateRuleRegistry() => new(new ISqlOptimizationRule[]
    {
        new SelectStarRule(),
        new FunctionOnColumnRule(),
        new NonSargablePredicateRule(),
        new LeadingWildcardLikeRule(),
        new ImplicitConversionRule(),
        new OrPredicateRule(),
        new CorrelatedSubqueryRule(),
        new NotInNullableRule(),
        new DistinctRule(),
        new UnionRule(),
        new RedundantOrderByRule(),
        new CartesianJoinRule(),
        new LeftJoinFilterRule(),
        new ExcessiveSubqueryRule(),
        new LargeInListRule(),
        new UnnecessaryCastRule(),
        new DuplicateExpressionRule(),
        new PotentialJoinExplosionRule(),
        new MissingJoinPredicateRule(),
        new ExcessiveFunctionUsageRule()
    });

    /// <summary>Builds an optimization request with the given options.</summary>
    internal static SqlOptimizationRequest Request(
        string sql,
        OptimizationOptions options,
        DatabaseSchema? schema = null) => new()
    {
        Sql = sql,
        Options = options,
        Schema = schema
    };

    /// <summary>
    /// Deterministic LLM test double: returns queued responses, records the
    /// requests it received, and simulates failures, delays and cancellation.
    /// </summary>
    internal sealed class FakeLlmProvider : ILlmClient
    {
        private readonly Queue<LlmResponse> _responses;

        public FakeLlmProvider(params LlmResponse[] responses) =>
            _responses = new Queue<LlmResponse>(responses);

        /// <summary>Requests received by the fake provider.</summary>
        public List<LlmRequest> Requests { get; } = [];

        /// <summary>Number of completed calls.</summary>
        public int CallCount => Requests.Count;

        /// <summary>When true, every call throws <see cref="Exception"/>.</summary>
        public bool ThrowOnCall { get; init; }

        /// <summary>The exception thrown when <see cref="ThrowOnCall"/> is set.</summary>
        public LlmException? Exception { get; init; }

        /// <summary>Artificial delay before responding (milliseconds).</summary>
        public int DelayMs { get; init; }

        /// <inheritdoc />
        public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (DelayMs > 0)
            {
                await Task.Delay(DelayMs, cancellationToken).ConfigureAwait(false);
            }

            if (ThrowOnCall)
            {
                throw Exception ?? new LlmException("Simulated LLM failure.");
            }

            return _responses.Count > 0 ? _responses.Dequeue() : new LlmResponse("{\"candidates\":[]}");
        }
    }

    /// <summary>
    /// Counting wrapper around the production validator: records every
    /// validation request and can simulate a validator failure.
    /// </summary>
    internal sealed class CountingValidator : ISqlValidator
    {
        private readonly SqlValidator _inner;

        public CountingValidator() => _inner = new SqlValidator(
            new SqlServerSqlParser(),
            new SqlOptimizerOptions(),
            new UnavailableDatabaseValidationProvider());

        /// <summary>Number of validation calls.</summary>
        public int CallCount { get; private set; }

        /// <summary>Requests received by the wrapped validator.</summary>
        public List<SqlValidationRequest> Requests { get; } = [];

        /// <summary>When true, every call throws.</summary>
        public bool ThrowOnCall { get; set; }

        /// <inheritdoc />
        public async Task<ValidationResult> ValidateAsync(
            SqlValidationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Requests.Add(request);
            if (ThrowOnCall)
            {
                throw new InvalidOperationException("Simulated validator failure.");
            }

            return await _inner.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
