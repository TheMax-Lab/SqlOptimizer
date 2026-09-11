using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Application.DTOs;

namespace SqlOptimizer.Application.Abstractions;

/// <summary>
/// A ready-to-send LLM prompt split into system and user parts.
/// </summary>
/// <param name="SystemPrompt">System prompt with role and hard constraints.</param>
/// <param name="UserPrompt">User prompt containing the query context and data sections.</param>
public sealed record LlmOptimizationPrompt(string SystemPrompt, string UserPrompt);

/// <summary>
/// Builds deterministic LLM prompts from analysis results. The generator
/// never calls an LLM itself; it only renders text.
/// </summary>
public interface IPromptGenerator
{
    /// <summary>
    /// Generates the optimization prompt for an analyzed query.
    /// </summary>
    /// <param name="analysis">The static analysis result.</param>
    /// <param name="context">The optimization context (options, plan, indexes...).</param>
    LlmOptimizationPrompt Generate(SqlAnalysis analysis, SqlOptimizationContext context);
}
