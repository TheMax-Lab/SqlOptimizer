using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Domain.Parsing;
using SqlOptimizer.Domain.Rules;

namespace SqlOptimizer.Application.Services;

/// <summary>
/// Deterministic SQL analysis pipeline:
/// SQL → parser → AST → <see cref="SqlAnalysisContext"/> → rules → findings
/// → deterministic scores → <see cref="SqlAnalysis"/>. The query is parsed
/// exactly once and the resulting AST is shared by every rule through the
/// context. No LLM, no randomness, no fake data: with the LLM disabled this
/// service is fully functional on its own.
/// </summary>
public sealed class SqlAnalyzer : ISqlAnalyzer
{
    private readonly ISqlParser _parser;
    private readonly RuleRegistry _ruleRegistry;
    private readonly IScoreEngine _scoreEngine;
    private readonly SqlOptimizerOptions _options;

    /// <summary>
    /// Creates a new analyzer.
    /// </summary>
    /// <param name="parser">The SQL parser (one dialect per implementation).</param>
    /// <param name="ruleRegistry">The registry of deterministic optimization rules.</param>
    /// <param name="scoreEngine">The deterministic score engine.</param>
    /// <param name="options">Global SqlOptimizer options (limits and rule thresholds).</param>
    public SqlAnalyzer(
        ISqlParser parser,
        RuleRegistry ruleRegistry,
        IScoreEngine scoreEngine,
        SqlOptimizerOptions options)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _ruleRegistry = ruleRegistry ?? throw new ArgumentNullException(nameof(ruleRegistry));
        _scoreEngine = scoreEngine ?? throw new ArgumentNullException(nameof(scoreEngine));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<SqlAnalysis> AnalyzeAsync(
        SqlAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Sql))
        {
            throw new SqlInvalidInputException("The SQL text must not be empty.");
        }

        if (request.Sql.Length > _options.MaxSqlLength)
        {
            throw new SqlInvalidInputException(
                $"The SQL text is {request.Sql.Length} characters long; the maximum is {_options.MaxSqlLength}.");
        }

        if (request.Dialect != _parser.Dialect)
        {
            throw new SqlUnsupportedDialectException(request.Dialect);
        }

        // The SQL is parsed exactly once; every rule reuses this AST.
        var parsed = _parser.Parse(request.Sql);

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        var context = new SqlAnalysisContext
        {
            Ast = parsed.Root,
            Dialect = request.Dialect,
            Schema = request.Schema,
            ExecutionPlan = ParseExecutionPlan(request.ExecutionPlan),
            Options = new RuleOptions(
                MaxSubqueryDepth: _options.Rules.MaxSubqueryDepth,
                LargeInListThreshold: _options.Rules.LargeInListThreshold,
                ExcessiveFunctionThreshold: _options.Rules.ExcessiveFunctionThreshold)
        };

        var findings = _ruleRegistry.Analyze(context);

        cancellationToken.ThrowIfCancellationRequested();

        var scores = _scoreEngine.Score(parsed.Root, findings);

        return new SqlAnalysis
        {
            Sql = request.Sql,
            Dialect = request.Dialect,
            Ast = parsed.Root,
            ComplexityScore = scores.ComplexityScore,
            PerformanceScore = scores.PerformanceScore,
            Findings = findings,
            Statistics = QueryStatistics.FromAst(parsed.Root)
        };
    }

    /// <summary>Parses an optional execution plan, or returns null.</summary>
    /// <param name="rawPlan">Raw plan XML when provided.</param>
    private static ExecutionPlan? ParseExecutionPlan(string? rawPlan) =>
        string.IsNullOrWhiteSpace(rawPlan) ? null : ExecutionPlanParser.Parse(rawPlan);
}