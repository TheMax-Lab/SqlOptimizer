namespace SqlOptimizer.Domain.AST;

/// <summary>
/// Renders AST expressions as deterministic canonical SQL text. Used for
/// finding duplicate expressions and for producing SQL fragments in findings.
/// The rendering is purely structural: identical structures always produce
/// identical text.
/// </summary>
public static class ExpressionNormalizer
{
    /// <summary>
    /// Renders an expression as canonical SQL text.
    /// </summary>
    /// <param name="expression">The expression to render.</param>
    public static string Normalize(SqlExpression expression)
    {
        return expression switch
        {
            ColumnExpression column => column.TableAlias is null
                ? column.Name
                : $"{column.TableAlias}.{column.Name}",

            LiteralExpression literal => literal.IsNull ? "NULL" : literal.Value,

            ParameterExpression parameter => parameter.Name,

            FunctionExpression function => RenderFunction(function),

            AggregateExpression aggregate => RenderAggregate(aggregate),

            BinaryExpression binary => $"{Normalize(binary.Left)} {FormatOperator(binary.Operator)} {Normalize(binary.Right)}",

            UnaryExpression unary => $"{FormatUnaryOperator(unary.Operator)} {Normalize(unary.Operand)}",

            InExpression inExpression => inExpression.HasSubquery
                ? $"{Normalize(inExpression.Expression)} {(inExpression.Not ? "NOT IN" : "IN")} (SELECT ...)"
                : $"{Normalize(inExpression.Expression)} {(inExpression.Not ? "NOT IN" : "IN")} ({string.Join(", ", inExpression.Values.Select(Normalize))})",

            ExistsExpression exists => $"{(exists.Not ? "NOT " : string.Empty)}EXISTS (SELECT ...)",

            LikeExpression like => $"{Normalize(like.Expression)} {(like.Not ? "NOT " : string.Empty)}LIKE {Normalize(like.Pattern)}",

            CaseExpression caseExpression => RenderCase(caseExpression),

            SubqueryExpression => "(SELECT ...)",

            CastExpression cast => RenderCast(cast),

            _ => expression.GetType().Name
        };
    }

    /// <summary>
    /// Renders an ORDER BY item as canonical text.
    /// </summary>
    /// <param name="item">The ORDER BY item.</param>
    public static string Normalize(OrderByItem item) =>
        $"{Normalize(item.Expression)} {(item.Ascending ? "ASC" : "DESC")}";

    /// <summary>
    /// Formats a binary operator as its SQL keyword/symbol.
    /// </summary>
    /// <param name="op">The operator.</param>
    private static string FormatOperator(SqlBinaryOperator op) => op switch
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
        _ => op.ToString()
    };

    /// <summary>
    /// Formats a unary operator as its SQL keyword/symbol.
    /// </summary>
    /// <param name="op">The operator.</param>
    private static string FormatUnaryOperator(SqlUnaryOperator op) => op switch
    {
        SqlUnaryOperator.Not => "NOT",
        SqlUnaryOperator.Negate => "-",
        _ => op.ToString()
    };

    /// <summary>Renders a scalar function expression.</summary>
    /// <param name="function">The function expression.</param>
    private static string RenderFunction(FunctionExpression function)
    {
        var text = $"{function.Name}({string.Join(", ", function.Arguments.Select(Normalize))})";
        return function.Window is not null ? text + RenderOver(function.Window) : text;
    }

    /// <summary>Renders an aggregate expression.</summary>
    /// <param name="aggregate">The aggregate expression.</param>
    private static string RenderAggregate(AggregateExpression aggregate)
    {
        string inner = aggregate.CountStar
            ? "*"
            : string.Join(", ", aggregate.Arguments.Select(Normalize));

        var text = $"{aggregate.FunctionName}({(aggregate.Distinct ? "DISTINCT " : string.Empty)}{inner})";
        return aggregate.Window is not null ? text + RenderOver(aggregate.Window) : text;
    }

    /// <summary>Renders a CASE expression.</summary>
    /// <param name="caseExpression">The CASE expression.</param>
    private static string RenderCase(CaseExpression caseExpression)
    {
        var parts = new List<string>
        {
            caseExpression.Operand is null ? "CASE" : $"CASE {Normalize(caseExpression.Operand)}"
        };

        foreach (var when in caseExpression.Whens)
        {
            parts.Add($"WHEN {Normalize(when.When)} THEN {Normalize(when.Then)}");
        }

        if (caseExpression.Else is not null)
        {
            parts.Add($"ELSE {Normalize(caseExpression.Else)}");
        }

        parts.Add("END");
        return string.Join(" ", parts);
    }

    /// <summary>Renders a data conversion expression according to its kind.</summary>
    /// <param name="cast">The cast expression.</param>
    private static string RenderCast(CastExpression cast)
    {
        return cast.Kind switch
        {
            CastKind.Convert => $"CONVERT({cast.TargetType}, {Normalize(cast.Expression)})",
            CastKind.TryConvert => $"TRY_CONVERT({cast.TargetType}, {Normalize(cast.Expression)})",
            _ => $"CAST({Normalize(cast.Expression)} AS {cast.TargetType})"
        };
    }

    /// <summary>Renders an OVER clause.</summary>
    /// <param name="window">The window function clause.</param>
    private static string RenderOver(WindowFunction window)
    {
        var parts = new List<string>();

        if (window.PartitionBy.Count > 0)
        {
            parts.Add($"PARTITION BY {string.Join(", ", window.PartitionBy.Select(Normalize))}");
        }

        if (window.OrderBy.Count > 0)
        {
            parts.Add($"ORDER BY {string.Join(", ", window.OrderBy.Select(Normalize))}");
        }

        if (!string.IsNullOrWhiteSpace(window.FrameText))
        {
            parts.Add(window.FrameText);
        }

        return $" OVER ({string.Join(" ", parts)})";
    }
}

