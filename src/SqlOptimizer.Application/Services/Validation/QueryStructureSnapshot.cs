using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Metadata;

namespace SqlOptimizer.Application.Services.Validation;

/// <summary>
/// A deterministic structural projection of a parsed SELECT statement, used
/// to compare the original query with a candidate. Built exclusively from the
/// existing AST finder/walker infrastructure and canonical text rendering, so
/// the same input always produces the same snapshot. The snapshot is a
/// comparison aid: it can prove structural facts, but it never proves
/// semantic equivalence on its own.
/// </summary>
public sealed record QueryStructureSnapshot
{
    /// <summary>Base tables referenced anywhere in the statement, full names, deduplicated.</summary>
    public required IReadOnlyList<string> Tables { get; init; }

    /// <summary>Parameters referenced (@names), deduplicated.</summary>
    public required IReadOnlyList<string> Parameters { get; init; }

    /// <summary>Projected items in order.</summary>
    public required IReadOnlyList<ProjectionStructure> Projection { get; init; }

    /// <summary>True when SELECT DISTINCT is used.</summary>
    public required bool Distinct { get; init; }

    /// <summary>Canonical GROUP BY expressions in order.</summary>
    public required IReadOnlyList<string> GroupBy { get; init; }

    /// <summary>Canonical HAVING predicate, or null when absent.</summary>
    public required string? Having { get; init; }

    /// <summary>Canonical ORDER BY items in order.</summary>
    public required IReadOnlyList<string> OrderBy { get; init; }

    /// <summary>Canonical FROM structure including joins; subqueries are fingerprinted.</summary>
    public required string FromTree { get; init; }

    /// <summary>Number of joins in the FROM structure (including subquery FROMs).</summary>
    public required int JoinCount { get; init; }

    /// <summary>Join types in the FROM structure, in tree order.</summary>
    public required IReadOnlyList<string> JoinTypes { get; init; }

    /// <summary>True when the FROM structure contains an outer join.</summary>
    public required bool HasOuterJoin { get; init; }

    /// <summary>Set operators chained after this statement, in order (for example "UNION ALL").</summary>
    public required IReadOnlyList<string> SetOperators { get; init; }

    /// <summary>CTE names in order.</summary>
    public required IReadOnlyList<string> Ctes { get; init; }

    /// <summary>Total number of subqueries in the statement (any depth, any position).</summary>
    public required int SubqueryCount { get; init; }

    /// <summary>True when at least one subquery is correlated.</summary>
    public required bool HasCorrelatedSubquery { get; init; }

    /// <summary>Canonical CAST/CONVERT/TRY_CONVERT expressions, in order.</summary>
    public required IReadOnlyList<string> Casts { get; init; }

    /// <summary>True when the statement contains a NOT IN predicate over a subquery.</summary>
    public required bool HasNotInSubquery { get; init; }

    /// <summary>True when the statement contains a NOT EXISTS predicate.</summary>
    public required bool HasNotExists { get; init; }

    /// <summary>True when the statement contains at least one aggregate function.</summary>
    public required bool HasAggregates { get; init; }

    /// <summary>Canonical WHERE predicate, or null when absent.</summary>
    public required string? Where { get; init; }

    /// <summary>
    /// The single base table of the statement when FROM is exactly one base
    /// table (no join, no derived table); null otherwise. Used to prove
    /// star-projection expansion against schema metadata.
    /// </summary>
    public required string? SingleBaseTable { get; init; }

    /// <summary>The qualifier (alias or name) of <see cref="SingleBaseTable"/>, when set.</summary>
    public required string? SingleBaseTableQualifier { get; init; }

    /// <summary>Single canonical fingerprint of the whole statement (set chain included).</summary>
    public required string Fingerprint { get; init; }

    /// <summary>
    /// Builds the snapshot for a parsed statement.
    /// </summary>
    /// <param name="statement">The parsed SELECT statement root.</param>
    public static QueryStructureSnapshot Build(SelectStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        var tables = TableReferenceFinder.FindTables(statement)
            .Select(t => t.FullName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var parameters = SqlAstWalker.OfType<ParameterExpression>(statement)
            .Select(p => p.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var projection = statement.SelectItems
            .Select(item => new ProjectionStructure(
                item.IsStar ? string.Empty : CanonicalExpression(item.Expression!),
                item.Alias,
                item.IsStar,
                item.StarTableAlias,
                item.IsStar ? null : item.Expression))
            .ToList();

        var joins = statement.From is null
            ? []
            : JoinFinder.FindJoins(statement.From.Source).ToList();

        var subqueries = SubqueryFinder.FindSubqueries(statement).ToList();

        string? singleBaseTable = null;
        string? singleBaseTableQualifier = null;
        if (statement.From?.Source is TableReference single)
        {
            singleBaseTable = single.FullName;
            singleBaseTableQualifier = single.Qualifier;
        }

        return new QueryStructureSnapshot
        {
            Tables = tables,
            Parameters = parameters,
            Projection = projection,
            Distinct = statement.Distinct,
            GroupBy = statement.GroupBy.Select(CanonicalExpression).ToList(),
            Having = statement.Having is null ? null : CanonicalPredicate(statement.Having),
            OrderBy = statement.OrderBy.Select(item =>
                $"{CanonicalExpression(item.Expression)} {(item.Ascending ? "ASC" : "DESC")}").ToList(),
            FromTree = statement.From is null ? "<none>" : RenderFromTree(statement.From.Source),
            JoinCount = joins.Count,
            JoinTypes = joins.Select(j => j.Type.ToString()).ToList(),
            HasOuterJoin = joins.Any(j => j.Type is not (JoinType.Inner or JoinType.Cross)),
            SetOperators = CollectSetOperators(statement).ToList(),
            Ctes = statement.Ctes.Select(c => c.Name).ToList(),
            SubqueryCount = subqueries.Count,
            HasCorrelatedSubquery = subqueries.Any(s => SubqueryFinder.IsCorrelated(statement, s)),
            Casts = FunctionFinder.FindCasts(statement).Select(CanonicalExpression).ToList(),
            HasNotInSubquery = SqlAstWalker.OfType<InExpression>(statement)
                .Any(i => i.Not && i.HasSubquery),
            HasNotExists = SqlAstWalker.OfType<ExistsExpression>(statement).Any(e => e.Not),
            HasAggregates = FunctionFinder.FindAggregates(statement).Any(),
            Where = statement.Where is null ? null : CanonicalPredicate(statement.Where),
            SingleBaseTable = singleBaseTable,
            SingleBaseTableQualifier = singleBaseTableQualifier,
            Fingerprint = ComputeFingerprint(statement)
        };
    }

    /// <summary>
    /// Renders a predicate canonically: top-level AND/OR chains are flattened
    /// and their operands sorted, so <c>a AND b</c> and <c>b AND a</c> produce
    /// identical text. Everything else is rendered with the canonical
    /// expression rendering. This is a structural normalization only; it never
    /// proves logical equivalence of arbitrary predicates.
    /// </summary>
    /// <param name="expression">The predicate to canonicalize.</param>
    public static string CanonicalPredicate(SqlExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        if (expression is not BinaryExpression binary ||
            binary.Operator is not (SqlBinaryOperator.And or SqlBinaryOperator.Or))
        {
            return CanonicalExpression(expression);
        }

        var operands = new List<SqlExpression>();
        CollectBooleanOperands(binary, binary.Operator, operands);

        var normalized = operands
            .Select(CanonicalExpression)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        return $"({string.Join($" {BooleanOperatorName(binary.Operator)} ", normalized)})";
    }

    /// <summary>
    /// Renders an expression as canonical comparison text: identifiers are
    /// case-folded (SQL Server identifiers are case-insensitive by default),
    /// keywords and operators use fixed casing, and literal values are kept
    /// verbatim because they are data, not identifiers.
    /// </summary>
    /// <param name="expression">The expression to render.</param>
    internal static string CanonicalExpression(SqlExpression expression) => expression switch
    {
        ColumnExpression column => column.TableAlias is null
            ? column.Name.ToLowerInvariant()
            : $"{column.TableAlias.ToLowerInvariant()}.{column.Name.ToLowerInvariant()}",

        LiteralExpression literal => literal.IsNull ? "NULL" : literal.Value,

        ParameterExpression parameter => parameter.Name.ToLowerInvariant(),

        FunctionExpression function => $"{function.Name.ToUpperInvariant()}({string.Join(", ", function.Arguments.Select(CanonicalExpression))})"
            + (function.Window is null ? string.Empty : RenderOver(function.Window)),

        AggregateExpression aggregate =>
            $"{aggregate.FunctionName.ToUpperInvariant()}({(aggregate.Distinct ? "DISTINCT " : string.Empty)}{(aggregate.CountStar ? "*" : string.Join(", ", aggregate.Arguments.Select(CanonicalExpression)))})"
            + (aggregate.Window is null ? string.Empty : RenderOver(aggregate.Window)),

        BinaryExpression binary => $"{CanonicalExpression(binary.Left)} {OperatorName(binary.Operator)} {CanonicalExpression(binary.Right)}",

        UnaryExpression unary => $"{UnaryOperatorName(unary.Operator)} {CanonicalExpression(unary.Operand)}",

        InExpression inExpr => inExpr.HasSubquery
            ? $"{CanonicalExpression(inExpr.Expression)} {(inExpr.Not ? "NOT IN" : "IN")} (SUBQUERY {ComputeFingerprint(inExpr.Subquery!.Statement)})"
            : $"{CanonicalExpression(inExpr.Expression)} {(inExpr.Not ? "NOT IN" : "IN")} ({string.Join(", ", inExpr.Values.Select(CanonicalExpression))})",

        ExistsExpression exists => $"{(exists.Not ? "NOT " : string.Empty)}EXISTS (SUBQUERY {ComputeFingerprint(exists.Subquery.Statement)})",

        LikeExpression like => $"{CanonicalExpression(like.Expression)} {(like.Not ? "NOT " : string.Empty)}LIKE {CanonicalExpression(like.Pattern)}",

        CaseExpression caseExpr => RenderCase(caseExpr),

        SubqueryExpression subquery => $"(SUBQUERY {ComputeFingerprint(subquery.Statement)})",

        CastExpression cast => cast.Kind switch
        {
            CastKind.Convert => $"CONVERT({cast.TargetType.ToUpperInvariant()}, {CanonicalExpression(cast.Expression)})",
            CastKind.TryConvert => $"TRY_CONVERT({cast.TargetType.ToUpperInvariant()}, {CanonicalExpression(cast.Expression)})",
            _ => $"CAST({CanonicalExpression(cast.Expression)} AS {cast.TargetType.ToUpperInvariant()})"
        },

        _ => expression.GetType().Name
    };

    /// <summary>Collects the operands of a flat AND/OR chain (recursing only into same-operator children).</summary>
    private static void CollectBooleanOperands(BinaryExpression node, SqlBinaryOperator op, List<SqlExpression> operands)
    {
        if (node.Left is BinaryExpression left && left.Operator == op)
        {
            CollectBooleanOperands(left, op, operands);
        }
        else
        {
            operands.Add(node.Left);
        }

        if (node.Right is BinaryExpression right && right.Operator == op)
        {
            CollectBooleanOperands(right, op, operands);
        }
        else
        {
            operands.Add(node.Right);
        }
    }

    /// <summary>
    /// Builds a canonical fingerprint of a whole statement, including the set
    /// operation chain. Two statements with the same fingerprint are
    /// structurally identical after canonicalization.
    /// </summary>
    /// <param name="statement">The statement to fingerprint.</param>
    internal static string ComputeFingerprint(SelectStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        var single = string.Join(" || ", new[]
        {
            statement.Distinct ? "DISTINCT" : "ALL",
            "SELECT " + string.Join(", ", statement.SelectItems.Select(ProjectionItemText)),
            "FROM " + (statement.From is null ? "<none>" : RenderFromTree(statement.From.Source)),
            statement.Where is null ? "WHERE <none>" : $"WHERE {CanonicalPredicate(statement.Where)}",
            "GROUP BY " + string.Join(", ", statement.GroupBy.Select(CanonicalExpression)),
            statement.Having is null ? "HAVING <none>" : $"HAVING {CanonicalPredicate(statement.Having)}",
            "ORDER BY " + string.Join(", ", statement.OrderBy.Select(item =>
                $"{CanonicalExpression(item.Expression)} {(item.Ascending ? "ASC" : "DESC")}")),
            "CTES " + string.Join(",", statement.Ctes.Select(c => $"{c.Name}={ComputeFingerprint(c.Query)}"))
        });

        return statement.SetOperation is { } setOperation
            ? $"{single} {SetOperatorName(setOperation.Operator)} {ComputeFingerprint(setOperation.Right)}"
            : single;
    }

    /// <summary>Walks the set operation chain collecting operator names in order.</summary>
    private static IEnumerable<string> CollectSetOperators(SelectStatement statement)
    {
        for (var setOperation = statement.SetOperation; setOperation is not null; setOperation = setOperation.Right.SetOperation)
        {
            yield return SetOperatorName(setOperation.Operator);
        }
    }

    /// <summary>Canonical FROM tree: tables by full name, joins with type and ON predicate, subqueries by fingerprint.</summary>
    private static string RenderFromTree(FromSource source) => source switch
    {
        TableReference table => table.FullName.ToLowerInvariant(),
        SubquerySource subquery => $"(SUBQUERY AS {subquery.Alias?.ToLowerInvariant() ?? "?"}={ComputeFingerprint(subquery.Query)})",
        JoinSource join => $"{RenderFromTree(join.Left)} {join.Type} {RenderFromTree(join.Right)}"
            + (join.Predicate is null ? " [CROSS]" : $" [ON {CanonicalExpression(join.Predicate)}]"),
        _ => source.GetType().Name
    };

    /// <summary>Renders one SELECT list item canonically.</summary>
    private static string ProjectionItemText(SelectItem item) => item.IsStar
        ? item.StarTableAlias is null ? "*" : $"{item.StarTableAlias.ToLowerInvariant()}.*"
        : $"{CanonicalExpression(item.Expression!)}{(item.Alias is null ? string.Empty : $" AS {item.Alias.ToLowerInvariant()}")}";

    /// <summary>Renders an ORDER BY item canonically.</summary>
    private static string OrderByItemText(OrderByItem item) =>
        $"{CanonicalExpression(item.Expression)} {(item.Ascending ? "ASC" : "DESC")}";

    /// <summary>Renders a CASE expression canonically.</summary>
    private static string RenderCase(CaseExpression caseExpression)
    {
        var parts = new List<string>
        {
            caseExpression.Operand is null ? "CASE" : $"CASE {CanonicalExpression(caseExpression.Operand)}"
        };

        foreach (var when in caseExpression.Whens)
        {
            parts.Add($"WHEN {CanonicalExpression(when.When)} THEN {CanonicalExpression(when.Then)}");
        }

        if (caseExpression.Else is not null)
        {
            parts.Add($"ELSE {CanonicalExpression(caseExpression.Else)}");
        }

        parts.Add("END");
        return string.Join(" ", parts);
    }

    /// <summary>Renders an OVER clause canonically.</summary>
    private static string RenderOver(WindowFunction window)
    {
        var parts = new List<string>();

        if (window.PartitionBy.Count > 0)
        {
            parts.Add($"PARTITION BY {string.Join(", ", window.PartitionBy.Select(CanonicalExpression))}");
        }

        if (window.OrderBy.Count > 0)
        {
            parts.Add($"ORDER BY {string.Join(", ", window.OrderBy.Select(OrderByItemText))}");
        }

        if (!string.IsNullOrWhiteSpace(window.FrameText))
        {
            parts.Add(window.FrameText);
        }

        return $" OVER ({string.Join(" ", parts)})";
    }

    /// <summary>Names a binary operator canonically.</summary>
    private static string OperatorName(SqlBinaryOperator op) => op switch
    {
        SqlBinaryOperator.Equal => "=",
        SqlBinaryOperator.NotEqual => "<>",
        SqlBinaryOperator.GreaterThan => ">",
        SqlBinaryOperator.GreaterThanOrEqual => ">=",
        SqlBinaryOperator.LessThan => "<",
        SqlBinaryOperator.LessThanOrEqual => "<=",
        SqlBinaryOperator.And => "AND",
        SqlBinaryOperator.Or => "OR",
        SqlBinaryOperator.Add => "+",
        SqlBinaryOperator.Subtract => "-",
        SqlBinaryOperator.Multiply => "*",
        SqlBinaryOperator.Divide => "/",
        SqlBinaryOperator.Is => "IS",
        SqlBinaryOperator.IsNot => "IS NOT",
        _ => op.ToString().ToUpperInvariant()
    };

    /// <summary>Names a unary operator canonically.</summary>
    private static string UnaryOperatorName(SqlUnaryOperator op) => op switch
    {
        SqlUnaryOperator.Not => "NOT",
        SqlUnaryOperator.Negate => "-",
        _ => op.ToString().ToUpperInvariant()
    };

    /// <summary>Names a boolean operator for canonical predicates.</summary>
    private static string BooleanOperatorName(SqlBinaryOperator op) =>
        op == SqlBinaryOperator.And ? "AND" : "OR";

    /// <summary>Names a set operator canonically.</summary>
    private static string SetOperatorName(SetOperatorKind kind) => kind switch
    {
        SetOperatorKind.UnionAll => "UNION ALL",
        SetOperatorKind.Union => "UNION",
        SetOperatorKind.Intersect => "INTERSECT",
        _ => kind.ToString().ToUpperInvariant()
    };
}
