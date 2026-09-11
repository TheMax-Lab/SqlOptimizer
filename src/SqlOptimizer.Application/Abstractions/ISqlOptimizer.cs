using SqlOptimizer.Application.DTOs;

namespace SqlOptimizer.Application.Abstractions;

/// <summary>
/// Entry point of the optimization pipeline: analyze → optimization plan →
/// recommendations → prompt → optional LLM candidates → optional validation.
/// Works fully without an LLM.
/// </summary>
public interface ISqlOptimizer
{
    /// <summary>
    /// Optimizes a SQL query.
    /// </summary>
    /// <param name="request">Optimization request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SqlOptimizationResult> OptimizeAsync(
        SqlOptimizationRequest request,
        CancellationToken cancellationToken = default);
}
