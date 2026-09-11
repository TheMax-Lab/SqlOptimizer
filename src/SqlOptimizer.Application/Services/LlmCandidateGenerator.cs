using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Domain.Common;

namespace SqlOptimizer.Application.Services;

/// <summary>
/// LLM candidate generator. Sends the deterministic evidence (analysis,
/// findings, plan, schema) through the prompt generator to an
/// <see cref="ILlmClient"/> and parses the response defensively. The LLM is
/// one candidate source among equals: it can propose, never validate. Every
/// candidate it returns has status <c>Generated</c> and is marked as
/// LLM-generated; <c>ISqlValidator</c> is the only component that may decide
/// its semantic status. All provider failures degrade gracefully to an empty
/// result with an explicit limitation (never to fabricated candidates).
/// </summary>
public sealed class LlmCandidateGenerator : ISqlOptimizationCandidateGenerator
{
    private readonly ILlmClient _client;
    private readonly IPromptGenerator _promptGenerator;
    private readonly LlmResponseParser _parser;
    private readonly LlmOptions _options;
    private readonly SqlOptimizerOptions _sqlOptions;

    /// <summary>Creates the generator.</summary>
    /// <param name="client">The LLM provider client.</param>
    /// <param name="promptGenerator">The deterministic prompt generator.</param>
    /// <param name="parser">The defensive LLM response parser.</param>
    /// <param name="options">LLM provider options.</param>
    /// <param name="sqlOptions">Global SqlOptimizer options (candidate caps).</param>
    public LlmCandidateGenerator(
        ILlmClient client,
        IPromptGenerator promptGenerator,
        LlmResponseParser parser,
        LlmOptions options,
        SqlOptimizerOptions sqlOptions)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _promptGenerator = promptGenerator ?? throw new ArgumentNullException(nameof(promptGenerator));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sqlOptions = sqlOptions ?? throw new ArgumentNullException(nameof(sqlOptions));
    }

    /// <inheritdoc />
    public async Task<CandidateGenerationResult> GenerateAsync(
        SqlOptimizationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!context.Options.UseLlm)
        {
            return CandidateGenerationResult.None;
        }

        if (string.IsNullOrWhiteSpace(_options.Provider))
        {
            return new CandidateGenerationResult(
                [],
                ["LLM is not configured (Llm:Provider is empty); no LLM candidates were generated. " +
                 "Deterministic candidates and rule recommendations remain available."]);
        }

        var prompt = _promptGenerator.Generate(context.Analysis, context);
        var promptLength = prompt.SystemPrompt.Length + prompt.UserPrompt.Length;
        if (promptLength > _options.MaxPromptChars)
        {
            return new CandidateGenerationResult(
                [],
                [$"LLM prompt is {promptLength} characters, above the {_options.MaxPromptChars} cap; LLM candidates were skipped."]);
        }

        LlmResponse response;
        try
        {
            response = await _client.CompleteAsync(
                new LlmRequest(
                    prompt.SystemPrompt,
                    prompt.UserPrompt,
                    _options.Temperature,
                    _options.MaxCompletionTokens,
                    _options.Model),
                cancellationToken).ConfigureAwait(false);
        }
        catch (LlmNotConfiguredException ex)
        {
            return new CandidateGenerationResult([], [$"LLM is not configured: {ex.Message}"]);
        }
        catch (LlmException ex)
        {
            return new CandidateGenerationResult([], [$"LLM call failed: {ex.Message}"]);
        }

        var maxCandidates = Math.Clamp(context.Options.MaxCandidates, 1, _sqlOptions.MaxCandidates);
        var parsed = _parser.Parse(response.Content, maxCandidates);
        if (parsed is null)
        {
            return new CandidateGenerationResult(
                [],
                ["The LLM response could not be parsed into usable candidates " +
                 "(malformed, rejected or empty). No LLM candidates were generated."]);
        }

        var ruleIds = context.Analysis.Findings
            .Select(f => f.RuleId)
            .Distinct()
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var candidates = new List<OptimizationCandidate>(parsed.Candidates.Count);
        for (var i = 0; i < parsed.Candidates.Count; i++)
        {
            var proposal = parsed.Candidates[i];
            candidates.Add(new OptimizationCandidate(
                CandidateId: $"LLM-{i + 1}",
                OriginalSql: context.OriginalSql,
                CandidateSql: proposal.Sql,
                Source: CandidateSource.Llm,
                RuleIds: ruleIds,
                Explanation: string.IsNullOrWhiteSpace(proposal.Explanation)
                    ? "LLM-proposed rewrite (no explanation provided)."
                    : proposal.Explanation!,
                ExpectedOptimization: proposal.ExpectedImpact,
                Confidence: proposal.Confidence,
                Warnings: proposal.Warnings,
                Assumptions: proposal.Assumptions,
                Limitations: ["LLM-generated proposal: semantic safety is decided exclusively by validation."],
                Status: CandidateStatus.Generated));
        }

        return new CandidateGenerationResult(candidates, parsed.Rejections);
    }
}
