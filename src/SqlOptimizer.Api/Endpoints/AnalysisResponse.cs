using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Common;

namespace SqlOptimizer.Api.Endpoints;

/// <summary>
/// Thin HTTP adapter over the Application/domain <see cref="SqlAnalysis"/>
/// result. It performs no analysis and applies no business rule: it only
/// selects which fields to expose and honors the request's <c>IncludeAst</c>
/// flag so the (large) parsed AST is not serialized by default.
/// </summary>
/// <param name="Sql">The analyzed SQL text.</param>
/// <param name="Dialect">The dialect of the analyzed query.</param>
/// <param name="ComplexityScore">Deterministic complexity score (0-100).</param>
/// <param name="PerformanceScore">Deterministic performance risk score (0-100).</param>
/// <param name="Findings">Findings produced by the rule engine.</param>
/// <param name="Statistics">Structural statistics of the query.</param>
/// <param name="Ast">The parsed AST, only when the request asked for it.</param>
public sealed record AnalysisResponse(
    string Sql,
    SqlDialect Dialect,
    int ComplexityScore,
    int PerformanceScore,
    IReadOnlyList<SqlFinding> Findings,
    QueryStatistics Statistics,
    SelectStatement? Ast)
{
    /// <summary>
    /// Projects a domain <see cref="SqlAnalysis"/> onto the HTTP response.
    /// </summary>
    /// <param name="analysis">The analysis result.</param>
    /// <param name="includeAst">Whether to include the parsed AST.</param>
    public static AnalysisResponse FromAnalysis(SqlAnalysis analysis, bool includeAst) => new(
        analysis.Sql,
        analysis.Dialect,
        analysis.ComplexityScore,
        analysis.PerformanceScore,
        analysis.Findings,
        analysis.Statistics,
        includeAst ? analysis.Ast : null);
}
