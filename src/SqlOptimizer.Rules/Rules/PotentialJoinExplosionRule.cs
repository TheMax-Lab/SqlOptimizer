using System.Globalization;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Support;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL018 — potential join row explosion. Requires schema metadata: for a
/// join between two known base tables, the rule checks whether any join key
/// is a unique key. When no join key is unique, the join can multiply rows;
/// the finding is a risk flag (confidence depends on available row counts)
/// and never claims a proven explosion.
/// </summary>
public sealed class PotentialJoinExplosionRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL018";

    /// <inheritdoc />
    public override string Name => "Potential join explosion";

    /// <inheritdoc />
    public override bool CanAnalyze(SqlAnalysisContext context) => context.Schema is not null;

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        foreach (var join in JoinFinder.FindJoins(context.Ast))
        {
            if (!join.HasMeaningfulPredicate || join.Type == JoinType.Cross)
            {
                continue;
            }

            var leftTable = RuleUtilities.GetSingleTable(join.Left);
            var rightTable = RuleUtilities.GetSingleTable(join.Right);
            if (leftTable is null || rightTable is null)
            {
                continue;
            }

            var leftMeta = context.FindTable(leftTable);
            var rightMeta = context.FindTable(rightTable);
            if (leftMeta is null || rightMeta is null)
            {
                continue;
            }

            var leftKeys = RuleUtilities.CollectQualifiers(join.Left);
            var rightKeys = RuleUtilities.CollectQualifiers(join.Right);

            var leftColumns = ColumnReferenceFinder.FindColumns(join.Predicate!)
                .Where(c => leftKeys.Contains(c.TableAlias ?? string.Empty))
                .Select(c => c.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var rightColumns = ColumnReferenceFinder.FindColumns(join.Predicate!)
                .Where(c => rightKeys.Contains(c.TableAlias ?? string.Empty))
                .Select(c => c.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (leftColumns.Count == 0 || rightColumns.Count == 0)
            {
                continue;
            }

            var hasUniqueKey = leftColumns.Any(leftMeta.IsUniqueKey)
                || rightColumns.Any(rightMeta.IsUniqueKey);

            if (hasUniqueKey)
            {
                continue;
            }

            var rowsKnown = leftMeta.EstimatedRowCount is not null
                && rightMeta.EstimatedRowCount is not null;

            var sizeInfo = rowsKnown
                ? $" Estimated sizes: {FormatRows(leftMeta.EstimatedRowCount)} × {FormatRows(rightMeta.EstimatedRowCount)}."
                : string.Empty;

            yield return CreateFinding(
                Severity.Warning,
                FindingCategory.Cardinality,
                $"Join between '{leftTable.Name}' and '{rightTable.Name}' on non-unique columns can multiply rows.{sizeInfo}",
                $"{leftTable.Name} JOIN {rightTable.Name}",
                explanation: "None of the join key columns is a primary key or unique index column, so the join can return up to the product of matching row counts. Without statistics (row counts, cardinality, histograms) this cannot be confirmed, so this is a risk flag, not a diagnosis. Verify with row counts or the actual execution plan before changing anything.",
                recommendations: new[]
                {
                    "Check the estimated and actual row counts in the execution plan.",
                    "If the business logic expects a 1:1 relationship, enforce uniqueness (unique index) or aggregate before the join."
                },
                confidence: rowsKnown ? 0.6 : 0.45,
                impact: new OptimizationImpact(Performance: 6, Readability: 0, Maintainability: 1, Risk: 2));
        }
    }

    private static string FormatRows(long? rows) =>
        rows?.ToString("n0", CultureInfo.InvariantCulture) ?? "unknown";
}