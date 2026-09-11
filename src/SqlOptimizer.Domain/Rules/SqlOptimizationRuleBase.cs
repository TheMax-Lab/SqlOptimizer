using SqlOptimizer.Domain.Analysis;

namespace SqlOptimizer.Domain.Rules;

/// <summary>
/// Convenience base class for <see cref="ISqlOptimizationRule"/> implementations.
/// Provides a uniform way to create <see cref="SqlFinding"/> objects with the
/// rule identifier pre-filled, so findings stay consistent across rules.
/// Rules must remain stateless, deterministic and side-effect free.
/// </summary>
public abstract class SqlOptimizationRuleBase : ISqlOptimizationRule
{
    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public virtual bool CanAnalyze(SqlAnalysisContext context) => true;

    /// <inheritdoc />
    public abstract IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context);

    /// <summary>
    /// Creates a finding attributed to this rule.
    /// </summary>
    /// <param name="severity">Severity of the issue.</param>
    /// <param name="category">Category of the issue.</param>
    /// <param name="message">Short human readable description.</param>
    /// <param name="sqlFragment">SQL fragment the finding refers to, when available.</param>
    /// <param name="explanation">Why this is an issue and what may or may not result from it.</param>
    /// <param name="recommendations">Suggested actions (suggestions, not guarantees).</param>
    /// <param name="confidence">0-1 confidence that the finding applies.</param>
    /// <param name="impact">Estimated impact dimensions.</param>
    protected SqlFinding CreateFinding(
        Severity severity,
        FindingCategory category,
        string message,
        string? sqlFragment = null,
        string? explanation = null,
        IReadOnlyList<string>? recommendations = null,
        double confidence = 0.5,
        OptimizationImpact? impact = null) => new()
    {
        RuleId = Id,
        Severity = severity,
        Category = category,
        Message = message,
        SqlFragment = sqlFragment,
        Explanation = explanation,
        Recommendations = recommendations ?? [],
        Confidence = Math.Clamp(confidence, 0.0, 1.0),
        Impact = impact ?? new OptimizationImpact(0, 0, 0, 0)
    };
}