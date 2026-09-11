using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Support;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL004 — LIKE pattern with a leading wildcard (<c>'%abc'</c>,
/// <c>'%abc%'</c>, <c>'_abc'</c>). A leading wildcard prevents index seeks
/// on the column. Trailing wildcards (<c>'abc%'</c>) are sargable and are not
/// reported. Patterns that are not string literals (parameters, expressions)
/// cannot be evaluated deterministically and are skipped.
/// </summary>
public sealed class LeadingWildcardLikeRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL004";

    /// <inheritdoc />
    public override string Name => "Leading wildcard in LIKE pattern";

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        foreach (var statement in RuleUtilities.EnumerateStatements(context.Ast))
        {
            foreach (var like in SqlAstWalker.DirectNodes(statement).OfType<LikeExpression>())
            {
                if (like.Pattern is not LiteralExpression { IsNull: false } pattern)
                {
                    continue;
                }

                var value = StripQuotes(pattern.Value);
                if (value.Length == 0 || (value[0] != '%' && value[0] != '_'))
                {
                    continue;
                }

                yield return CreateFinding(
                    Severity.Warning,
                    FindingCategory.Sargability,
                    $"LIKE pattern '{value}' starts with a wildcard.",
                    $"{PredicatePositions.Describe(like.Expression)} LIKE '{value}'",
                    explanation: "A leading wildcard prevents the use of an index seek on the column, because index ordering cannot be exploited when the match may start anywhere in the value. On large tables this typically means a scan; on small tables the difference may be negligible.",
                    recommendations: new[]
                    {
                        "If suffix search is required, consider a full-text index or a dedicated search structure.",
                        "If only prefix matches are needed, rewrite the pattern without a leading wildcard.",
                        "Keep the pattern when suffix search is a business requirement and the table is small."
                    },
                    confidence: 0.9,
                    impact: new OptimizationImpact(Performance: 5, Readability: 0, Maintainability: 0, Risk: 1));
            }
        }
    }

    /// <summary>Removes surrounding single quotes from a string literal.</summary>
    private static string StripQuotes(string value) =>
        value.Trim().Trim('\'');
}