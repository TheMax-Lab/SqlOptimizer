using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Support;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL016 — unnecessary CAST/CONVERT. Only no-op conversions are reported:
/// a CAST nested inside another CAST to the same type, or (with schema
/// metadata) a CAST of a column to its own type. Casts to different types
/// are legitimate and are never flagged. This is a maintainability finding,
/// not a performance claim.
/// </summary>
public sealed class UnnecessaryCastRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL016";

    /// <inheritdoc />
    public override string Name => "Unnecessary CAST/CONVERT";

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        foreach (var statement in RuleUtilities.EnumerateStatements(context.Ast))
        {
            foreach (var cast in SqlAstWalker.DirectNodes(statement).OfType<CastExpression>())
            {
                var fragment = ExpressionNormalizer.Normalize(cast);

                if (cast.Expression is CastExpression inner &&
                    RuleUtilities.NormalizeType(cast.TargetType) == RuleUtilities.NormalizeType(inner.TargetType))
                {
                    yield return CreateFinding(
                        Severity.Info,
                        FindingCategory.Maintainability,
                        "Nested conversion to the same type.",
                        fragment,
                        explanation: "Converting a value to the same type it already has does not change the result; it only adds noise (and, for TRY_CONVERT, a failure check). This is a readability issue, not a performance problem.",
                        recommendations: new[]
                        {
                            "Remove the redundant nested conversion."
                        },
                        confidence: 0.9,
                        impact: new OptimizationImpact(Performance: 0, Readability: 3, Maintainability: 3, Risk: 0));
                    continue;
                }

                if (context.Schema is not null &&
                    cast.Expression is ColumnExpression column)
                {
                    var resolved = RuleUtilities.ResolveColumn(context, statement, column);
                    if (resolved is not null &&
                        RuleUtilities.NormalizeType(resolved.DataType) == RuleUtilities.NormalizeType(cast.TargetType))
                    {
                        yield return CreateFinding(
                            Severity.Info,
                            FindingCategory.Maintainability,
                            $"Conversion of column '{column.Name}' to its own type.",
                            fragment,
                            explanation: $"According to the schema, the column is already of type '{resolved.DataType}', so the conversion is a no-op. Removing it does not change the result; keep it only if it documents an intentional type contract.",
                            recommendations: new[]
                            {
                                "Remove the redundant conversion.",
                                "Keep it only if it documents an intentional type contract."
                            },
                            confidence: 0.85,
                            impact: new OptimizationImpact(Performance: 0, Readability: 3, Maintainability: 3, Risk: 0));
                    }
                }
            }
        }
    }
}