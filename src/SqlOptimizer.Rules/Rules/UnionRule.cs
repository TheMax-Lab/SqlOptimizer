using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL010 — UNION (deduplicating set operation). Reports that UNION adds a
/// de-duplication pass and that UNION ALL may be possible when duplicates
/// between branches are impossible or irrelevant. The different duplicate
/// semantics are always made explicit; the change is never asserted as safe.
/// </summary>
public sealed class UnionRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL010";

    /// <inheritdoc />
    public override string Name => "UNION de-duplication";

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        var setOperations = SqlAstWalker.OfType<SetOperation>(context.Ast)
            .Where(op => op.Operator == SetOperatorKind.Union)
            .ToList();

        for (var index = 0; index < setOperations.Count; index++)
        {
            yield return CreateFinding(
                Severity.Warning,
                FindingCategory.Performance,
                $"UNION (#{index + 1}) removes duplicate rows between the branches.",
                "UNION",
                explanation: "UNION adds a de-duplication step (sort or hash) over the combined result. If duplicates between the branches are impossible (for example mutually exclusive filters) or irrelevant to the consumer, UNION ALL keeps all rows and skips the de-duplication. The two operators have different result semantics: UNION ALL can return rows that UNION would collapse, so the switch is only correct when the data guarantees no duplicates — that guarantee depends on the data, not on the query.",
                recommendations: new[]
                {
                    "Verify that the branches cannot produce identical rows before switching to UNION ALL.",
                    "Check the execution plan for a Sort/Hash Match used only for de-duplication."
                },
                confidence: 0.4,
                impact: new OptimizationImpact(Performance: 4, Readability: 0, Maintainability: 0, Risk: 3));
        }
    }
}