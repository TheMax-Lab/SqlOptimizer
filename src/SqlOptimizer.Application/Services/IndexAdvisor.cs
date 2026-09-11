using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Domain.Optimization;
using SqlOptimizer.Domain.Rules;

namespace SqlOptimizer.Application.Services;

/// <summary>
/// Heuristic index advisor. It inspects the outer statement of the query only
/// (subquery scopes cannot be resolved reliably without full name resolution)
/// and proposes index candidates based on predicate shape, join columns,
/// ORDER BY and GROUP BY. When schema metadata is available, recommendations
/// that an existing index already satisfies are suppressed. All results are
/// heuristic and confidence-scored: they are never guaranteed optimizations.
/// </summary>
public sealed class IndexAdvisor : IIndexAdvisor
{
    private const int MaxKeyColumns = 3;
    private const int MaxIncludedColumns = 4;
    private const double MinConfidence = 0.45;

    /// <summary>Produces index recommendations for the given context.</summary>
    /// <param name="context">The analysis context.</param>
    public IReadOnlyList<IndexRecommendation> Recommend(SqlAnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var root = context.Ast;
        var aliases = TableReferenceFinder.GetTableAliases(root);
        if (aliases.Count == 0)
        {
            return [];
        }

        var evidence = new Dictionary<(string Table, string Column), ColumnEvidence>(
            CaseInsensitiveKeyComparer.Instance);

        CollectPredicateEvidence(root, aliases, evidence);
        CollectJoinEvidence(root, aliases, evidence);
        CollectOrderGroupEvidence(root, aliases, evidence);

        var recommendations = new List<IndexRecommendation>();
        foreach (var group in evidence
            .GroupBy(kv => kv.Key.Table, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var recommendation = BuildRecommendation(
                group.Key,
                group.Select(kv => kv.Value).ToList(),
                root, aliases, context);
            if (recommendation is not null)
            {
                recommendations.Add(recommendation);
            }
        }

        return recommendations
            .OrderByDescending(r => r.EstimatedBenefit)
            .ThenBy(r => r.Table, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Collects equality/range/LIKE evidence from WHERE and HAVING predicates.
    /// </summary>
    private static void CollectPredicateEvidence(
        SelectStatement root,
        IReadOnlyDictionary<string, TableReference> aliases,
        Dictionary<(string Table, string Column), ColumnEvidence> evidence)
    {
        foreach (var predicate in SqlExpressionFinder.FindAtomicPredicates(root))
        {
            switch (predicate)
            {
                case BinaryExpression { IsComparison: true } comparison:
                    if (!TryGetComparableColumn(comparison, aliases, out var column) || column.Table is null)
                    {
                        continue;
                    }

                    // column.Table is proven non-null by TryGetComparableColumn.
                    var key = (Table: column.Table!, Column: column.Column);
                    if (comparison.Operator == SqlBinaryOperator.Equal)
                    {
                        Add(evidence, key, e => e.Equality = true);
                    }
                    else if (comparison.Operator is
                        SqlBinaryOperator.GreaterThan or
                        SqlBinaryOperator.GreaterThanOrEqual or
                        SqlBinaryOperator.LessThan or
                        SqlBinaryOperator.LessThanOrEqual)
                    {
                        Add(evidence, key, e => e.Range = true);
                    }

                    break;

                case LikeExpression like:
                    if (like.Expression is ColumnExpression { TableAlias: not null } likeColumn &&
                        aliases.TryGetValue(likeColumn.TableAlias!, out var likeTable) &&
                        like.Pattern is LiteralExpression { IsString: true } pattern &&
                        !pattern.Value.StartsWith("%", StringComparison.Ordinal))
                    {
                        Add(evidence, (likeTable.Qualifier, likeColumn.Name), e => e.Like = true);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Collects join equality evidence: ON a.Id = b.CustomerId adds candidates
    /// on both sides.
    /// </summary>
    private static void CollectJoinEvidence(
        SelectStatement root,
        IReadOnlyDictionary<string, TableReference> aliases,
        Dictionary<(string Table, string Column), ColumnEvidence> evidence)
    {
        if (root.From is null)
        {
            return;
        }

        foreach (var join in JoinFinder.FindJoins(root.From.Source, includeSubqueries: false))
        {
            if (join.Predicate is null)
            {
                continue;
            }

            foreach (var atomic in SqlExpressionFinder.FindAtomicPredicates(new SelectStatement(
                [], null, join.Predicate, [], null, [], false)))
            {
                if (atomic is not BinaryExpression
                    {
                        Operator: SqlBinaryOperator.Equal,
                        Left: ColumnExpression { TableAlias: not null } left,
                        Right: ColumnExpression { TableAlias: not null } right
                    })
                {
                    continue;
                }

                if (aliases.TryGetValue(left.TableAlias!, out var leftTable))
                {
                    Add(evidence, (leftTable.Qualifier, left.Name), e => e.Join = true);
                }

                if (aliases.TryGetValue(right.TableAlias!, out var rightTable))
                {
                    Add(evidence, (rightTable.Qualifier, right.Name), e => e.Join = true);
                }
            }
        }
    }

    /// <summary>
    /// Collects ORDER BY and GROUP BY column evidence.
    /// </summary>
    private static void CollectOrderGroupEvidence(
        SelectStatement root,
        IReadOnlyDictionary<string, TableReference> aliases,
        Dictionary<(string Table, string Column), ColumnEvidence> evidence)
    {
        foreach (var order in root.OrderBy)
        {
            if (order.Expression is ColumnExpression { TableAlias: not null } column &&
                aliases.TryGetValue(column.TableAlias!, out var table))
            {
                Add(evidence, (table.Qualifier, column.Name), e => e.Order = true);
            }
        }

        foreach (var group in root.GroupBy)
        {
            if (group is ColumnExpression { TableAlias: not null } column &&
                aliases.TryGetValue(column.TableAlias!, out var table))
            {
                Add(evidence, (table.Qualifier, column.Name), e => e.Group = true);
            }
        }
    }

    /// <summary>
    /// Extracts the column side of a comparison (the other side must be a
    /// literal, parameter or case expression so an index could be used).
    /// </summary>
    private static bool TryGetComparableColumn(
        BinaryExpression comparison,
        IReadOnlyDictionary<string, TableReference> aliases,
        out (string? Table, string Column) column)
    {
        column = default;

        if (IsFilterValue(comparison.Left) &&
            comparison.Right is ColumnExpression { TableAlias: not null } right)
        {
            column = Resolve(right, aliases);
            return column.Table is not null;
        }

        if (IsFilterValue(comparison.Right) &&
            comparison.Left is ColumnExpression { TableAlias: not null } left)
        {
            column = Resolve(left, aliases);
            return column.Table is not null;
        }

        return false;

        static (string? Table, string Column) Resolve(
            ColumnExpression column,
            IReadOnlyDictionary<string, TableReference> aliases) =>
            aliases.TryGetValue(column.TableAlias!, out var table)
                ? (table.Qualifier, column.Name)
                : (null, column.Name);

        static bool IsFilterValue(SqlExpression expression) =>
            expression is LiteralExpression or ParameterExpression or CaseExpression;
    }

    /// <summary>
    /// Builds one recommendation per table from the collected evidence.
    /// </summary>
    private static IndexRecommendation? BuildRecommendation(
        string qualifier,
        IReadOnlyList<ColumnEvidence> columns,
        SelectStatement root,
        IReadOnlyDictionary<string, TableReference> aliases,
        SqlAnalysisContext context)
    {
        if (!aliases.TryGetValue(qualifier, out var table))
        {
            return null;
        }

        var keyColumns = columns
            .OrderBy(c => c.FirstSeen)
            .ThenBy(c => c.Column, StringComparer.OrdinalIgnoreCase)
            .Select(c => c.Column)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxKeyColumns)
            .ToList();

        var confidence = 0.35;
        foreach (var column in columns)
        {
            if (column.Equality)
            {
                confidence += 0.2;
            }

            if (column.Join)
            {
                confidence += 0.15;
            }

            if (column.Range)
            {
                confidence += 0.1;
            }

            if (column.Like)
            {
                confidence += 0.05;
            }

            if (column.Order)
            {
                confidence += 0.05;
            }

            if (column.Group)
            {
                confidence += 0.05;
            }
        }

        confidence = Math.Min(confidence, 0.95);
        if (confidence < MinConfidence)
        {
            return null;
        }

        var benefit = 40;
        if (columns.Any(c => c.Equality))
        {
            benefit += 15;
        }

        if (columns.Any(c => c.Range))
        {
            benefit += 10;
        }

        if (columns.Any(c => c.Join))
        {
            benefit += 10;
        }

        if (columns.Any(c => c.Like))
        {
            benefit += 5;
        }

        if (columns.Any(c => c.Order))
        {
            benefit += 5;
        }

        var includedColumns = SelectColumnsOfTable(root, table)
            .Where(c => !keyColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .Take(MaxIncludedColumns)
            .ToList();

        var recommendation = new IndexRecommendation
        {
            Table = table.FullName,
            KeyColumns = keyColumns,
            IncludedColumns = includedColumns,
            Reason = BuildReason(columns),
            Confidence = Math.Round(confidence, 2),
            EstimatedBenefit = Math.Min(benefit, 90)
        };

        return IsAlreadyCovered(context, table, recommendation) ? null : recommendation;
    }

    /// <summary>
    /// Collects the SELECT columns that belong to the given table.
    /// </summary>
    private static IEnumerable<string> SelectColumnsOfTable(SelectStatement root, TableReference table)
    {
        foreach (var item in root.SelectItems)
        {
            foreach (var column in SqlAstWalker.OfType<ColumnExpression>(item))
            {
                if (string.Equals(column.TableAlias, table.Qualifier, StringComparison.OrdinalIgnoreCase))
                {
                    yield return column.Name;
                }
            }
        }
    }

    /// <summary>
    /// Builds the human readable reason for a recommendation.
    /// </summary>
    private static string BuildReason(IReadOnlyList<ColumnEvidence> columns)
    {
        var parts = new List<string>();
        if (columns.Any(c => c.Equality))
        {
            parts.Add("equality filter");
        }

        if (columns.Any(c => c.Range))
        {
            parts.Add("range filter");
        }

        if (columns.Any(c => c.Join))
        {
            parts.Add("join predicate");
        }

        if (columns.Any(c => c.Like))
        {
            parts.Add("prefix LIKE filter");
        }

        if (columns.Any(c => c.Order))
        {
            parts.Add("ORDER BY");
        }

        if (columns.Any(c => c.Group))
        {
            parts.Add("GROUP BY");
        }

        return "Heuristic: column appears in " +
               string.Join(", ", parts) +
               ". An index may allow a seek instead of a scan; verify with workload statistics.";
    }

    /// <summary>
    /// True when an existing index on the table already starts with the
    /// proposed key columns (in the same order), making the recommendation
    /// redundant.
    /// </summary>
    private static bool IsAlreadyCovered(
        SqlAnalysisContext context,
        TableReference table,
        IndexRecommendation recommendation)
    {
        var metadata = context.FindTable(table);
        if (metadata is null)
        {
            return false;
        }

        return metadata.Indexes.Any(index =>
            recommendation.KeyColumns.Count <= index.KeyColumns.Count &&
            recommendation.KeyColumns
                .Select((column, i) => (column, i))
                .All(pair => string.Equals(
                    index.KeyColumns[pair.i], pair.column, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Adds (or updates) evidence for a (table, column) pair.
    /// </summary>
    private static void Add(
        Dictionary<(string Table, string Column), ColumnEvidence> evidence,
        (string Table, string Column) key,
        Action<ColumnEvidence> update)
    {
        if (!evidence.TryGetValue(key, out var column))
        {
            column = new ColumnEvidence { FirstSeen = evidence.Count };
            evidence[key] = column;
        }

        update(column);
    }

    /// <summary>Mutable evidence accumulator for one (table, column) pair.</summary>
    private sealed class ColumnEvidence
    {
        public int FirstSeen { get; init; }

        public string Column { get; set; } = "";

        public bool Equality { get; set; }

        public bool Range { get; set; }

        public bool Like { get; set; }

        public bool Join { get; set; }

        public bool Order { get; set; }

        public bool Group { get; set; }
    }

    /// <summary>Case-insensitive comparer for (table, column) dictionary keys.</summary>
    private sealed class CaseInsensitiveKeyComparer : IEqualityComparer<(string Table, string Column)>
    {
        public static readonly CaseInsensitiveKeyComparer Instance = new();

        public bool Equals((string Table, string Column) x, (string Table, string Column) y) =>
            string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Column, y.Column, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Table, string Column) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Column));
    }
}
