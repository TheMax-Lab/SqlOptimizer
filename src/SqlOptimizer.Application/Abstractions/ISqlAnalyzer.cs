using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Application.DTOs;

namespace SqlOptimizer.Application.Abstractions;

/// <summary>
/// Entry point of the analysis pipeline: SQL → parse → AST → rules →
/// findings → deterministic scores. The query is parsed exactly once.
/// </summary>
public interface ISqlAnalyzer
{
    /// <summary>
    /// Analyzes a SQL query.
    /// </summary>
    /// <param name="request">Analysis request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SqlAnalysis> AnalyzeAsync(
        SqlAnalysisRequest request,
        CancellationToken cancellationToken = default);
}
