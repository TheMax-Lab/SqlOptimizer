using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Metadata;

namespace SqlOptimizer.Application.Services;

/// <summary>
/// Deterministic candidate generator. It emits candidates only for rewrites
/// that are safe by construction and that the validator can prove: currently
/// the expansion of a sole <c>SELECT *</c> projection into the explicit
/// column list, and only when schema metadata covers the table. A rule
/// without a safe mechanical rewrite produces a recommendation, never a
/// candidate. Every candidate is returned with status <c>Generated</c>; only
/// <c>ISqlValidator</c> can move it to <c>Validated</c>.
/// </summary>
public sealed class DeterministicCandidateGenerator : ISqlOptimizationCandidateGenerator
{
    /// <inheritdoc />
    public Task<CandidateGenerationResult> GenerateAsync(
        SqlOptimizationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = new List<OptimizationCandidate>();
        if (TryBuildStarExpansionCandidate(context, out var candidate) && candidate is not null)
        {
            candidates.Add(candidate);
        }

        return Task.FromResult(new CandidateGenerationResult(candidates, []));
    }

    /// <summary>
    /// Builds the SELECT * expansion candidate when the query is a single
    /// statement with a sole star projection over a single base table whose
    /// schema is known, or reports failure (candidate stays null).
    /// </summary>
    private static bool TryBuildStarExpansionCandidate(
        SqlOptimizationContext context,
        out OptimizationCandidate? candidate)
    {
        candidate = null;

        var root = context.Analysis.Ast;

        // Structural guards: outer statement only, sole star item, no set
        // operation, no CTEs, single base table source.
        if (root.SelectItems.Count != 1 || root.SelectItems[0] is not { IsStar: true } star)
        {
            return false;
        }

        if (root.SetOperation is not null || root.Ctes.Count > 0)
        {
            return false;
        }

        if (root.From is not { Source: TableReference table })
        {
            return false;
        }

        if (context.Schema is null)
        {
            return false;
        }

        var tableInfo = context.Schema.FindTable(table);
        if (tableInfo is null || tableInfo.Columns.Count == 0)
        {
            return false;
        }

        // Expand to qualified names when the star is qualified; otherwise to
        // plain column names. No new alias is ever introduced.
        var expansion = string.Join(
            ", ",
            tableInfo.Columns.Select(c => star.StarTableAlias is null ? c.Name : $"{table.Qualifier}.{c.Name}"));

        var candidateSql = TryReplaceStar(context.OriginalSql, star, expansion);
        if (candidateSql is null)
        {
            return false;
        }

        candidate = new OptimizationCandidate(
            CandidateId: "DET-SQL001",
            OriginalSql: context.OriginalSql,
            CandidateSql: candidateSql,
            Source: CandidateSource.Rule,
            RuleIds: ["SQL001"],
            Explanation:
                $"Expanded the star projection to the explicit column list of '{tableInfo.Schema}.{tableInfo.Name}' " +
                $"({tableInfo.Columns.Count} columns) using schema metadata.",
            ExpectedOptimization:
                "A stable, explicit result contract; narrower I/O and covering-index friendly.",
            Confidence: 0.9,
            Warnings: [],
            Assumptions: [$"Schema metadata lists all columns of {tableInfo.Schema}.{tableInfo.Name} in definition order."],
            Limitations: [],
            Status: CandidateStatus.Generated);

        return true;
    }

    /// <summary>
    /// Replaces the star token in the original SQL text with the explicit
    /// column list. The AST already proves the star is the only projection
    /// item and the table is the only FROM source; the text anchors are just
    /// positioning aids. Returns null when the token cannot be located, which
    /// suppresses the candidate (never a partial rewrite).
    /// </summary>
    private static string? TryReplaceStar(string originalSql, SelectItem star, string expansion)
    {
        if (star.StarTableAlias is { } alias)
        {
            var fragment = alias + ".*";
            var index = FindIdentifierOccurrence(originalSql, fragment);
            if (index < 0)
            {
                return null;
            }

            return originalSql[..index] + expansion + originalSql[(index + fragment.Length)..];
        }

        var selectIndex = FindKeyword(originalSql, "SELECT");
        if (selectIndex < 0)
        {
            return null;
        }

        var fromIndex = FindKeyword(originalSql, "FROM", selectIndex + "SELECT".Length);
        if (fromIndex < 0)
        {
            return null;
        }

        var searchFrom = selectIndex + "SELECT".Length;
        for (var i = searchFrom; i < fromIndex; i++)
        {
            if (originalSql[i] != '*')
            {
                continue;
            }

            // The star must be a standalone token, not part of an identifier.
            if (i > searchFrom && IsIdentifierChar(originalSql[i - 1]))
            {
                continue;
            }

            return originalSql[..i] + expansion + originalSql[(i + 1)..];
        }

        return null;
    }

    /// <summary>
    /// Finds an identifier-shaped fragment (for example <c>t.*</c>) in the
    /// SQL text with a word-boundary check on the preceding character.
    /// </summary>
    private static int FindIdentifierOccurrence(string sql, string fragment)
    {
        var index = sql.IndexOf(fragment, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            if (index == 0 || !IsIdentifierChar(sql[index - 1]))
            {
                return index;
            }

            index = sql.IndexOf(fragment, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return -1;
    }

    /// <summary>Finds a SQL keyword as a whole word at or after <paramref name="startIndex"/>.</summary>
    private static int FindKeyword(string sql, string keyword, int startIndex = 0)
    {
        var index = sql.IndexOf(keyword, startIndex, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var end = index + keyword.Length;
            if ((index == 0 || !IsIdentifierChar(sql[index - 1])) &&
                (end >= sql.Length || !IsIdentifierChar(sql[end])))
            {
                return index;
            }

            index = sql.IndexOf(keyword, end, StringComparison.OrdinalIgnoreCase);
        }

        return -1;
    }

    /// <summary>True for characters that can appear in a SQL identifier.</summary>
    private static bool IsIdentifierChar(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '$' or '@' or '#';
}
