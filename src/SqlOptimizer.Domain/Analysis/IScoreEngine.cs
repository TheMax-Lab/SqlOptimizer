using SqlOptimizer.Domain.AST;

namespace SqlOptimizer.Domain.Analysis;

/// <summary>
/// Deterministic scores produced for an analyzed query. Both scores are
/// integers from 0 to 100 where 0 is best.
/// </summary>
/// <param name="ComplexityScore">Structural complexity, 0 (simplest) to 100 (most complex).</param>
/// <param name="PerformanceScore">Performance risk, 0 (no risk) to 100 (worst).</param>
public sealed record ScoreResult(int ComplexityScore, int PerformanceScore);

/// <summary>
/// Contract for the deterministic score engine. Implementations must be pure:
/// same input always produces the same output.
/// </summary>
public interface IScoreEngine
{
    /// <summary>
    /// Computes deterministic complexity and performance risk scores.
    /// </summary>
    /// <param name="ast">The parsed statement.</param>
    /// <param name="findings">Findings produced by the rule engine.</param>
    ScoreResult Score(SelectStatement ast, IReadOnlyList<SqlFinding> findings);
}
