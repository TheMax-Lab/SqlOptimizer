using SqlOptimizer.Domain.Analysis;

namespace SqlOptimizer.Domain.Rules;

/// <summary>
/// Contract for a SQL optimization rule. A rule inspects the shared AST
/// (never re-parses SQL) and returns zero or more findings. Rules must be
/// deterministic and side-effect free.
/// </summary>
public interface ISqlOptimizationRule
{
    /// <summary>Unique rule identifier (for example <c>SQL001</c>).</summary>
    string Id { get; }

    /// <summary>Human readable rule name.</summary>
    string Name { get; }

    /// <summary>
    /// True when the rule can produce findings for the given context
    /// (for example because required metadata is available).
    /// </summary>
    /// <param name="context">The analysis context.</param>
    bool CanAnalyze(SqlAnalysisContext context);

    /// <summary>
    /// Analyzes the context and returns findings. Must return an empty
    /// sequence (not throw) when no issues are detected.
    /// </summary>
    /// <param name="context">The analysis context.</param>
    IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context);
}
