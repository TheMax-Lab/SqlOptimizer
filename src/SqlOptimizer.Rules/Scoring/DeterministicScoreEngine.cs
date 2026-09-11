using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Analysis;

namespace SqlOptimizer.Rules.Scoring;

/// <summary>
/// Deterministic score engine. Both scores are integers from 0 to 100 where
/// 0 is best. The algorithm is pure: the same AST and findings always produce
/// the same scores, and no randomness or external state is used.
/// </summary>
/// <remarks>
/// <para>
/// <b>ComplexityScore</b> measures structural complexity from
/// <see cref="QueryStatistics"/> (capped terms keep single constructs from
/// saturating the scale):
/// <list type="bullet">
/// <item>base: 5</item>
/// <item>+ 4 per join (cap 10 joins = 40)</item>
/// <item>+ 5 per subquery (cap 8 = 40)</item>
/// <item>+ 3 per subquery nesting level (cap 6 = 18)</item>
/// <item>+ 2 per CTE (cap 5 = 10)</item>
/// <item>+ 5 when a set operation is used</item>
/// <item>+ 3 when SELECT DISTINCT is used</item>
/// <item>+ 1 per additional base table (cap 8)</item>
/// <item>+ 1 per function/cast usage (cap 10)</item>
/// <item>+ 1 per aggregate usage (cap 5)</item>
/// <item>+ 1 per ORDER BY item (cap 5)</item>
/// <item>+ 1 per predicate (cap 10)</item>
/// <item>+ 1 per SELECT list item (cap 10)</item>
/// </list>
/// The result is clamped to 0..100. A simple single-table query scores in
/// the single digits; heavily nested, multi-join queries saturate.
/// </para>
/// <para>
/// <b>PerformanceScore</b> measures performance risk from the rule findings.
/// Each finding contributes
/// <c>severityWeight × confidence × (1 + Impact.Performance / 10)</c> where
/// severityWeight is Info = 1, Warning = 3, High = 7, Critical = 12. The
/// sum is multiplied by 2 and clamped to 0..100. A query with no findings
/// scores 0; a single High finding scores roughly 15-25; several High/Critical
/// findings saturate the scale.
/// </para>
/// </remarks>
public sealed class DeterministicScoreEngine : IScoreEngine
{
    /// <inheritdoc />
    public ScoreResult Score(SelectStatement ast, IReadOnlyList<SqlFinding> findings)
    {
        if (ast is null)
        {
            throw new ArgumentNullException(nameof(ast));
        }

        if (findings is null)
        {
            throw new ArgumentNullException(nameof(findings));
        }

        var statistics = QueryStatistics.FromAst(ast);
        return new ScoreResult(ComputeComplexity(statistics), ComputePerformance(findings));
    }

    /// <summary>
    /// Computes the structural complexity score (see type remarks for the
    /// weights).
    /// </summary>
    /// <param name="statistics">Structural statistics of the query.</param>
    private static int ComputeComplexity(QueryStatistics statistics)
    {
        var score = 5;
        score += 4 * Math.Min(statistics.JoinCount, 10);
        score += 5 * Math.Min(statistics.SubqueryCount, 8);
        score += 3 * Math.Min(statistics.MaxSubqueryDepth, 6);
        score += 2 * Math.Min(statistics.CteCount, 5);

        if (statistics.HasUnion)
        {
            score += 5;
        }

        if (statistics.HasDistinct)
        {
            score += 3;
        }

        score += Math.Min(Math.Max(statistics.TableCount - 1, 0), 8);
        score += Math.Min(statistics.FunctionCount, 10);
        score += Math.Min(statistics.AggregateCount, 5);
        score += Math.Min(statistics.OrderByCount, 5);
        score += Math.Min(statistics.PredicateCount, 10);
        score += Math.Min(statistics.SelectColumnCount, 10);

        return Math.Clamp(score, 0, 100);
    }

    /// <summary>
    /// Computes the performance risk score from the findings (see type
    /// remarks for the formula).
    /// </summary>
    /// <param name="findings">Findings produced by the rule engine.</param>
    private static int ComputePerformance(IReadOnlyList<SqlFinding> findings)
    {
        var total = 0.0;

        foreach (var finding in findings)
        {
            var weight = finding.Severity switch
            {
                Severity.Info => 1,
                Severity.Warning => 3,
                Severity.High => 7,
                Severity.Critical => 12,
                _ => 0
            };

            total += weight * finding.Confidence * (1.0 + finding.Impact.Performance / 10.0);
        }

        return Math.Clamp((int)Math.Round(2 * total), 0, 100);
    }
}