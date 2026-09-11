using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Optimization;
using SqlOptimizer.Domain.Rules;

namespace SqlOptimizer.Application.Abstractions;

/// <summary>
/// Builds a deterministic optimization plan from analysis findings.
/// </summary>
public interface IOptimizationPlanBuilder
{
    /// <summary>
    /// Builds the plan for an analyzed query.
    /// </summary>
    /// <param name="analysis">The analysis result.</param>
    /// <param name="context">The analysis context (AST, schema, options).</param>
    /// <param name="strategy">Optimization strategy.</param>
    OptimizationPlan Build(SqlAnalysis analysis, SqlAnalysisContext context, OptimizationStrategy strategy);
}
