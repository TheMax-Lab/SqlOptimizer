using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Domain.Rules;

namespace SqlOptimizer.Application.Services;

/// <summary>
/// End-to-end optimization pipeline: analyze → deterministic plan →
/// recommendations → index hints → candidate generation (deterministic plus
/// optional LLM) → deduplication → mandatory validation → deterministic
/// ranking → result. The LLM is one candidate source among equals: it can
/// propose, never validate. Every generated candidate is passed through
/// <see cref="ISqlValidator"/>; there is no option to skip validation, and
/// <c>Inconclusive</c> is never reinterpreted as success.
/// </summary>
public sealed class SqlOptimizationService : ISqlOptimizer
{
    private readonly ISqlAnalyzer _analyzer;
    private readonly IOptimizationPlanBuilder _planBuilder;
    private readonly IIndexAdvisor _indexAdvisor;
    private readonly IReadOnlyCollection<ISqlOptimizationCandidateGenerator> _candidateGenerators;
    private readonly ISqlValidator _validator;
    private readonly CandidateRanker _ranker;
    private readonly IPromptGenerator _promptGenerator;
    private readonly SqlOptimizerOptions _options;

    /// <summary>Creates the optimizer.</summary>
    /// <param name="analyzer">The deterministic analyzer.</param>
    /// <param name="planBuilder">The deterministic plan builder.</param>
    /// <param name="indexAdvisor">The heuristic index advisor.</param>
    /// <param name="candidateGenerators">All registered candidate generators.</param>
    /// <param name="validator">The semantic-safety validator (mandatory for every candidate).</param>
    /// <param name="ranker">The deterministic candidate ranker.</param>
    /// <param name="promptGenerator">The prompt generator (exposes the prompt in the result).</param>
    /// <param name="options">Global SqlOptimizer options.</param>
    public SqlOptimizationService(
        ISqlAnalyzer analyzer,
        IOptimizationPlanBuilder planBuilder,
        IIndexAdvisor indexAdvisor,
        IEnumerable<ISqlOptimizationCandidateGenerator> candidateGenerators,
        ISqlValidator validator,
        CandidateRanker ranker,
        IPromptGenerator promptGenerator,
        SqlOptimizerOptions options)
    {
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _planBuilder = planBuilder ?? throw new ArgumentNullException(nameof(planBuilder));
        _indexAdvisor = indexAdvisor ?? throw new ArgumentNullException(nameof(indexAdvisor));
        _candidateGenerators = candidateGenerators?.ToList() ?? throw new ArgumentNullException(nameof(candidateGenerators));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _ranker = ranker ?? throw new ArgumentNullException(nameof(ranker));
        _promptGenerator = promptGenerator ?? throw new ArgumentNullException(nameof(promptGenerator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<SqlOptimizationResult> OptimizeAsync(
        SqlOptimizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Deterministic analysis: parse, rules, scores.
        var analysis = await _analyzer.AnalyzeAsync(new SqlAnalysisRequest
        {
            Sql = request.Sql,
            Dialect = request.Dialect,
            Schema = request.Schema,
            ExecutionPlan = request.ExecutionPlan
        }, cancellationToken).ConfigureAwait(false);

        var ruleContext = BuildRuleContext(analysis, request);
        var plan = _planBuilder.Build(analysis, ruleContext, request.Options.Strategy);
        var recommendations = BuildRecommendations(analysis);
        var indexes = request.Options.GenerateIndexes ? _indexAdvisor.Recommend(ruleContext) : [];

        var pipelineLimitations = new List<string>();
        var context = new SqlOptimizationContext(
            request.Sql,
            request.Dialect,
            analysis,
            request.Schema,
            request.ExecutionPlan,
            request.Options,
            plan,
            indexes);

        // 2. Candidate generation: deterministic first, then the optional LLM.
        var generated = new List<OptimizationCandidate>();
        foreach (var generator in _candidateGenerators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await generator.GenerateAsync(context, cancellationToken).ConfigureAwait(false);
            generated.AddRange(result.Candidates);
            pipelineLimitations.AddRange(result.Limitations);
        }

        // 3. Deduplicate candidates with identical normalized SQL.
        var unique = Deduplicate(generated, pipelineLimitations);

        // 4. Mandatory validation of every candidate.
        var validated = new List<OptimizationCandidate>(unique.Count);
        foreach (var candidate in unique)
        {
            cancellationToken.ThrowIfCancellationRequested();
            validated.Add(await ValidateCandidateAsync(request, candidate, pipelineLimitations, cancellationToken).ConfigureAwait(false));
        }

        // 5. Deterministic ranking.
        var ranked = _ranker.Rank(validated, analysis);

        // 6. Prompt exposure when requested (or when the LLM was used).
        var prompt = (request.Options.GeneratePrompt || request.Options.UseLlm)
            ? _promptGenerator.Generate(analysis, context)
            : null;

        return new SqlOptimizationResult(
            analysis,
            plan,
            recommendations,
            ranked,
            indexes,
            prompt,
            ranked.Count > 0 ? ranked[0].Validation : null)
        {
            Limitations = pipelineLimitations
        };
    }

    /// <summary>
    /// Validates one candidate. Validation is mandatory and unconditional:
    /// <c>Passed</c> → <c>Validated</c>, <c>Failed</c> → <c>Rejected</c>,
    /// anything else (Inconclusive, NotExecuted, NotRequested, or a validator
    /// exception) → <c>Inconclusive</c>, never reinterpreted as success.
    /// </summary>
    private async Task<OptimizationCandidate> ValidateCandidateAsync(
        SqlOptimizationRequest request,
        OptimizationCandidate candidate,
        List<string> pipelineLimitations,
        CancellationToken cancellationToken)
    {
        ValidationResult validation;
        try
        {
            validation = await _validator.ValidateAsync(new SqlValidationRequest(
                request.Sql,
                candidate.CandidateSql,
                CompareResults: request.Options.ValidateSemantics,
                MaxRowsForComparison: 1000,
                Dialect: request.Dialect,
                Schema: request.Schema),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            pipelineLimitations.Add(
                $"Validation of candidate {candidate.CandidateId} could not be executed ({ex.Message}); it is kept as inconclusive.");
            validation = new ValidationResult
            {
                Status = ValidationStatus.NotExecuted,
                Errors = [ex.Message],
                Limitations = ["Validation threw an exception; no semantic conclusion was reached."]
            };
        }

        var status = validation.Status switch
        {
            ValidationStatus.Passed => CandidateStatus.Validated,
            ValidationStatus.Failed => CandidateStatus.Rejected,
            _ => CandidateStatus.Inconclusive
        };

        return candidate with
        {
            Validation = validation,
            Status = status
        };
    }

    /// <summary>
    /// Deduplicates candidates whose normalized SQL is identical, keeping the
    /// first occurrence. Duplicates are reported as pipeline limitations.
    /// </summary>
    private static List<OptimizationCandidate> Deduplicate(
        List<OptimizationCandidate> candidates,
        List<string> pipelineLimitations)
    {
        var seen = new Dictionary<string, OptimizationCandidate>(StringComparer.Ordinal);
        var unique = new List<OptimizationCandidate>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var key = NormalizeForDedup(candidate.CandidateSql);
            if (seen.TryGetValue(key, out var first))
            {
                pipelineLimitations.Add(
                    $"Candidate {candidate.CandidateId} is a duplicate of {first.CandidateId} (normalized SQL) and was discarded.");
                continue;
            }

            seen.Add(key, candidate);
            unique.Add(candidate);
        }

        return unique;
    }

    /// <summary>Collapses whitespace runs into single spaces for the dedup key.</summary>
    private static string NormalizeForDedup(string sql) =>
        string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Rebuilds the rule context from the analysis (AST parsed once by the analyzer).</summary>
    private SqlAnalysisContext BuildRuleContext(SqlAnalysis analysis, SqlOptimizationRequest request) => new()
    {
        Ast = analysis.Ast,
        Dialect = request.Dialect,
        Schema = request.Schema,
        ExecutionPlan = ParsePlan(request.ExecutionPlan),
        Options = new RuleOptions(
            MaxSubqueryDepth: _options.Rules.MaxSubqueryDepth,
            LargeInListThreshold: _options.Rules.LargeInListThreshold,
            ExcessiveFunctionThreshold: _options.Rules.ExcessiveFunctionThreshold)
    };

    /// <summary>Parses an optional execution plan; the plan is auxiliary context, so failures yield null.</summary>
    private static ExecutionPlan? ParsePlan(string? rawPlan)
    {
        if (string.IsNullOrWhiteSpace(rawPlan))
        {
            return null;
        }

        try
        {
            return ExecutionPlanParser.Parse(rawPlan);
        }
        catch (SqlOptimizerException)
        {
            return null;
        }
    }

    /// <summary>Maps each finding to a recommendation (suggestion, never a guarantee).</summary>
    private static IReadOnlyList<OptimizationRecommendation> BuildRecommendations(SqlAnalysis analysis) =>
        analysis.Findings
            .Select(f => new OptimizationRecommendation(
                f.RuleId,
                f.Message,
                f.Severity,
                f.Category,
                f.SqlFragment,
                f.Recommendations,
                f.Confidence,
                f.Impact))
            .ToList();
}