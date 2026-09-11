using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Parsing;
using Sdom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlOptimizer.Infrastructure.Parsing;

/// <summary>
/// Converts a Microsoft ScriptDom T-SQL syntax tree into the SqlOptimizer
/// domain AST. Create one instance per parsed query: the converter
/// accumulates non-fatal warnings for constructs that the analysis model
/// does not fully represent.
/// </summary>
public sealed class ScriptDomToAstConverter
{
    /// <summary>
    /// T-SQL built-in aggregate function names. SQL Server exposes a closed
    /// set of built-in aggregates, so name based detection is reliable
    /// (user defined aggregates are the only false negatives).
    /// </summary>
    private static readonly HashSet<string> AggregateFunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "SUM", "AVG", "MIN", "MAX", "STDEV", "STDEVP", "VAR", "VARP",
        "STRING_AGG", "GROUPING", "CHECKSUM_AGG"
    };

    private readonly List<string> _warnings = [];

    /// <summary>
    /// Converts a parsed SELECT statement (including its WITH clause) into
    /// the domain AST.
    /// </summary>
    /// <param name="statement">The ScriptDom SELECT statement.</param>
    public ParsedQuery Convert(Sdom.SelectStatement statement)
    {
        var root = ConvertStatementLevel(statement);
        return new ParsedQuery(root, _warnings);
    }

    /// <summary>
    /// Converts a statement level node, attaching the CTEs declared in its
    /// WITH clause.
    /// </summary>
    private SelectStatement ConvertStatementLevel(Sdom.SelectStatement statement) =>
        ConvertQueryExpression(statement.QueryExpression) with
        {
            Ctes = ConvertCtes(statement.WithCtesAndXmlNamespaces)
        };

    /// <summary>
    /// Converts a query expression into the left-most SELECT statement of
    /// that expression tree. Set operations are chained onto it and the
    /// expression level ORDER BY / OFFSET apply to the whole result.
    /// </summary>
    private SelectStatement ConvertQueryExpression(Sdom.QueryExpression expression)
    {
        var leftmost = expression switch
        {
            Sdom.QuerySpecification specification => ConvertQuerySpecification(specification),
            Sdom.BinaryQueryExpression binary => ConvertBinaryQueryExpression(binary),
            Sdom.QueryParenthesisExpression parenthesis => ConvertQueryExpression(parenthesis.QueryExpression),
            _ => ThrowUnsupported<SelectStatement>(expression)
        };

        return ApplyQueryExpressionTail(leftmost, expression);
    }

    /// <summary>
    /// Applies the expression level ORDER BY to a converted statement.
    /// OFFSET/FETCH has no representation in the analysis model and is
    /// reported as a warning instead.
    /// </summary>
    private SelectStatement ApplyQueryExpressionTail(SelectStatement statement, Sdom.QueryExpression expression)
    {
        var orderBy = expression.OrderByClause;

        if (orderBy?.OrderByElements is { Count: > 0 } elements)
        {
            statement = statement with
            {
                OrderBy = elements.Select(e => new OrderByItem(
                    ConvertExpression(e.Expression),
                    e.SortOrder != Sdom.SortOrder.Descending)).ToList()
            };
        }
        else if (orderBy is { All: true })
        {
            AddWarning("ORDER BY ALL is not represented and was ignored.");
        }

        if (expression.OffsetClause is not null)
        {
            AddWarning("OFFSET/FETCH pagination is not represented and was ignored.");
        }

        return statement;
    }

    /// <summary>
    /// Converts a UNION / UNION ALL / INTERSECT / EXCEPT expression. The
    /// domain model chains set operations left-deep, so when the left side
    /// already ends a chain the new operation is attached to the right-most
    /// statement of that chain.
    /// </summary>
    private SelectStatement ConvertBinaryQueryExpression(Sdom.BinaryQueryExpression binary)
    {
        var left = ConvertQueryExpression(binary.FirstQueryExpression);
        var right = ConvertQueryExpression(binary.SecondQueryExpression);
        var newOperation = new SetOperation(MapSetOperator(binary), right);

        if (left.SetOperation is { } existing)
        {
            var rightmost = existing.Right with { SetOperation = newOperation };
            return left with { SetOperation = existing with { Right = rightmost } };
        }

        return left with { SetOperation = newOperation };
    }

    /// <summary>
    /// Maps a ScriptDom set operator to the domain kind.
    /// </summary>
    private static SetOperatorKind MapSetOperator(Sdom.BinaryQueryExpression binary) =>
        binary.BinaryQueryExpressionType switch
        {
            Sdom.BinaryQueryExpressionType.Union => binary.All ? SetOperatorKind.UnionAll : SetOperatorKind.Union,
            Sdom.BinaryQueryExpressionType.Intersect => SetOperatorKind.Intersect,
            Sdom.BinaryQueryExpressionType.Except => SetOperatorKind.Except,
            _ => throw new SqlParseException($"Unsupported set operator '{binary.BinaryQueryExpressionType}'.")
        };

    /// <summary>
    /// Converts a QuerySpecification (a single SELECT...FROM...WHERE block).
    /// </summary>
    private SelectStatement ConvertQuerySpecification(Sdom.QuerySpecification specification)
    {
        var selectItems = specification.SelectElements.Select(ConvertSelectElement).ToList();
        var from = specification.FromClause is { TableReferences.Count: > 0 } fromClause
            ? new FromClause(ConvertFromSources(fromClause.TableReferences))
            : null;
        var where = specification.WhereClause?.SearchCondition is { } whereCondition
            ? ConvertPredicate(whereCondition)
            : null;
        var groupBy = specification.GroupByClause is null
            ? []
            : specification.GroupByClause.GroupingSpecifications
                .SelectMany(FlattenGroupingSpecification)
                .ToList();
        var having = specification.HavingClause?.SearchCondition is { } havingCondition
            ? ConvertPredicate(havingCondition)
            : null;

        if (specification.TopRowFilter is not null)
        {
            AddWarning("TOP row filter is not represented and was ignored.");
        }

        return new SelectStatement(
            selectItems,
            from,
            where,
            groupBy,
            having,
            [],
            specification.UniqueRowFilter == Sdom.UniqueRowFilter.Distinct);
    }

    /// <summary>
    /// Converts one item of the SELECT list.
    /// </summary>
    private SelectItem ConvertSelectElement(Sdom.SelectElement element)
    {
        switch (element)
        {
            case Sdom.SelectScalarExpression scalar:
                return new SelectItem(ConvertExpression(scalar.Expression), GetAlias(scalar.ColumnName));

            case Sdom.SelectStarExpression star:
                return star.Qualifier is { } qualifier
                    ? SelectItem.Star(GetStarQualifier(qualifier))
                    : SelectItem.Star();

            case Sdom.SelectSetVariable setVariable:
                AddWarning($"SELECT list variable assignment '{setVariable.Variable.Name}' is not represented; only the assigned expression is analyzed.");
                return new SelectItem(ConvertExpression(setVariable.Expression), null);

            default:
                return ThrowUnsupported<SelectItem>(element);
        }
    }

    /// <summary>
    /// Extracts the output alias from a SELECT item column name, when present.
    /// </summary>
    private static string? GetAlias(Sdom.IdentifierOrValueExpression? alias) =>
        alias?.Identifier?.Value ?? alias?.Value;

    /// <summary>
    /// Extracts the table part of a <c>t.*</c> or <c>schema.table.*</c> star
    /// qualifier (the last identifier part).
    /// </summary>
    private static string GetStarQualifier(Sdom.MultiPartIdentifier qualifier) =>
        qualifier.Identifiers[^1].Value;

    /// <summary>
    /// Converts the FROM clause table reference list. Comma separated
    /// sources are folded into a left-deep chain of CROSS JOINs.
    /// </summary>
    private FromSource ConvertFromSources(IList<Sdom.TableReference> references)
    {
        var source = ConvertTableReference(references[0]);

        for (var i = 1; i < references.Count; i++)
        {
            if (i == 1)
            {
                AddWarning("Implicit comma separated FROM sources were mapped to CROSS JOINs.");
            }

            source = new JoinSource(source, ConvertTableReference(references[i]), JoinType.Cross, null);
        }

        return source;
    }

    /// <summary>
    /// Converts a FROM clause table reference of any kind.
    /// </summary>
    private FromSource ConvertTableReference(Sdom.TableReference reference)
    {
        switch (reference)
        {
            case Sdom.NamedTableReference named:
                return ConvertNamedTable(named);

            case Sdom.QueryDerivedTable derived:
                return new SubquerySource(
                    ConvertQueryExpression(derived.QueryExpression),
                    derived.Alias?.Value);

            case Sdom.QualifiedJoin join:
                return ConvertQualifiedJoin(join);

            case Sdom.UnqualifiedJoin unqualified:
                return ConvertUnqualifiedJoin(unqualified);

            case Sdom.JoinParenthesisTableReference parenthesis:
                return ConvertTableReference(parenthesis.Join);

            case Sdom.VariableTableReference variable:
                return new TableReference(string.Empty, variable.Variable.Name, variable.Alias?.Value);

            case Sdom.BuiltInFunctionTableReference function:
                AddWarning($"Table-valued function '{function.Name.Value}' in FROM was treated as a plain table reference.");
                return new TableReference(string.Empty, function.Name.Value, function.Alias?.Value);

            case Sdom.GlobalFunctionTableReference globalFunction:
                AddWarning($"Built-in table function '{globalFunction.Name.Value}' in FROM was treated as a plain table reference.");
                return new TableReference(string.Empty, globalFunction.Name.Value, globalFunction.Alias?.Value);

            default:
                return ThrowUnsupported<FromSource>(reference);
        }
    }

    /// <summary>
    /// Converts a base table reference. Cross-server / cross-database
    /// qualifiers are flattened to schema.table.
    /// </summary>
    private TableReference ConvertNamedTable(Sdom.NamedTableReference named)
    {
        var schemaObject = named.SchemaObject;

        if (schemaObject.ServerIdentifier is not null || schemaObject.DatabaseIdentifier is not null)
        {
            AddWarning("Cross-server/database table reference was flattened to schema.table.");
        }

        return new TableReference(
            schemaObject.SchemaIdentifier?.Value ?? string.Empty,
            schemaObject.BaseIdentifier?.Value ?? string.Empty,
            named.Alias?.Value);
    }

    /// <summary>
    /// Converts a qualified join (INNER / LEFT / RIGHT / FULL [OUTER] JOIN).
    /// </summary>
    private JoinSource ConvertQualifiedJoin(Sdom.QualifiedJoin join)
    {
        var type = join.QualifiedJoinType switch
        {
            Sdom.QualifiedJoinType.Inner => JoinType.Inner,
            Sdom.QualifiedJoinType.LeftOuter => JoinType.Left,
            Sdom.QualifiedJoinType.RightOuter => JoinType.Right,
            Sdom.QualifiedJoinType.FullOuter => JoinType.Full,
            _ => throw new SqlParseException($"Unsupported join type '{join.QualifiedJoinType}'.")
        };

        return new JoinSource(
            ConvertTableReference(join.FirstTableReference),
            ConvertTableReference(join.SecondTableReference),
            type,
            join.SearchCondition is { } condition ? ConvertPredicate(condition) : null);
    }

    /// <summary>
    /// Converts an unqualified join: CROSS JOIN, CROSS APPLY and OUTER
    /// APPLY. APPLY joins are represented as the closest regular join.
    /// </summary>
    private JoinSource ConvertUnqualifiedJoin(Sdom.UnqualifiedJoin join)
    {
        var left = ConvertTableReference(join.FirstTableReference);
        var right = ConvertTableReference(join.SecondTableReference);

        switch (join.UnqualifiedJoinType)
        {
            case Sdom.UnqualifiedJoinType.CrossJoin:
                return new JoinSource(left, right, JoinType.Cross, null);

            case Sdom.UnqualifiedJoinType.CrossApply:
                AddWarning("CROSS APPLY was mapped to CROSS JOIN.");
                return new JoinSource(left, right, JoinType.Cross, null);

            case Sdom.UnqualifiedJoinType.OuterApply:
                AddWarning("OUTER APPLY was mapped to LEFT JOIN.");
                return new JoinSource(left, right, JoinType.Left, null);

            default:
                throw new SqlParseException($"Unsupported join type '{join.UnqualifiedJoinType}'.");
        }
    }

    /// <summary>
    /// Converts the CTEs declared in a WITH clause.
    /// </summary>
    private IReadOnlyList<CommonTableExpression> ConvertCtes(Sdom.WithCtesAndXmlNamespaces? ctes)
    {
        var defined = ctes?.CommonTableExpressions;
        if (defined is null || defined.Count == 0)
        {
            return [];
        }

        return defined.Select(cte =>
            new CommonTableExpression(
                cte.ExpressionName.Value,
                cte.Columns?.Select(c => c.Value).ToList() ?? [],
                ConvertQueryExpression(cte.QueryExpression),
                false)).ToList();
    }

    /// <summary>
    /// Flattens a GROUP BY grouping specification into plain expressions.
    /// ROLLUP / CUBE / GROUPING SETS carry no representation in the
    /// analysis model and are flattened with a warning.
    /// </summary>
    private IEnumerable<SqlExpression> FlattenGroupingSpecification(Sdom.GroupingSpecification specification)
    {
        switch (specification)
        {
            case Sdom.ExpressionGroupingSpecification expression:
                yield return ConvertExpression(expression.Expression);
                break;

            case Sdom.RollupGroupingSpecification rollup:
                AddWarning("GROUP BY ROLLUP grouping sets were flattened to their plain expressions.");
                foreach (var flattened in FlattenArguments(rollup.Arguments))
                {
                    yield return flattened;
                }

                break;

            case Sdom.CubeGroupingSpecification cube:
                AddWarning("GROUP BY CUBE grouping sets were flattened to their plain expressions.");
                foreach (var flattened in FlattenArguments(cube.Arguments))
                {
                    yield return flattened;
                }

                break;

            case Sdom.GroupingSetsGroupingSpecification sets:
                AddWarning("GROUP BY GROUPING SETS were flattened to their plain expressions.");
                foreach (var flattened in FlattenArguments(sets.Sets))
                {
                    yield return flattened;
                }

                break;

            case Sdom.CompositeGroupingSpecification composite:
                foreach (var flattened in FlattenArguments(composite.Items))
                {
                    yield return flattened;
                }

                break;

            case Sdom.GrandTotalGroupingSpecification:
                AddWarning("GROUP BY ROLLUP() grand total was ignored.");
                break;

            default:
                throw new SqlParseException($"Unsupported GROUP BY specification '{specification.GetType().Name}'.");
        }
    }

    /// <summary>
    /// Flattens a list of grouping specification arguments into plain
    /// expressions.
    /// </summary>
    private IEnumerable<SqlExpression> FlattenArguments(IList<Sdom.GroupingSpecification> arguments)
    {
        foreach (var argument in arguments)
        {
            foreach (var flattened in FlattenGroupingSpecification(argument))
            {
                yield return flattened;
            }
        }
    }

    /// <summary>
    /// Converts a boolean expression (search condition) into an AST
    /// expression. BETWEEN is rewritten as a comparison pair because the
    /// analysis model has no dedicated BETWEEN node; the rewrite preserves
    /// three-valued NULL semantics.
    /// </summary>
    private SqlExpression ConvertPredicate(Sdom.BooleanExpression expression)
    {
        switch (expression)
        {
            case Sdom.BooleanBinaryExpression binary:
                return new BinaryExpression(
                    binary.BinaryExpressionType == Sdom.BooleanBinaryExpressionType.And
                        ? SqlBinaryOperator.And
                        : SqlBinaryOperator.Or,
                    ConvertPredicate(binary.FirstExpression),
                    ConvertPredicate(binary.SecondExpression));

            case Sdom.BooleanComparisonExpression comparison:
                return new BinaryExpression(
                    MapComparisonOperator(comparison.ComparisonType),
                    ConvertExpression(comparison.FirstExpression),
                    ConvertExpression(comparison.SecondExpression));

            case Sdom.BooleanParenthesisExpression parenthesis:
                return ConvertPredicate(parenthesis.Expression);

            case Sdom.BooleanNotExpression not:
                if (not.Expression is Sdom.ExistsPredicate negatedExists)
                {
                    return new ExistsExpression(
                        new SubqueryExpression(ConvertQueryExpression(negatedExists.Subquery.QueryExpression)),
                        true);
                }

                return new UnaryExpression(SqlUnaryOperator.Not, ConvertPredicate(not.Expression));

            case Sdom.BooleanIsNullExpression isNull:
                return new BinaryExpression(
                    isNull.IsNot ? SqlBinaryOperator.IsNot : SqlBinaryOperator.Is,
                    ConvertExpression(isNull.Expression),
                    new LiteralExpression("NULL", "null"));

            case Sdom.BooleanTernaryExpression between:
                return ConvertBetween(between);

            case Sdom.InPredicate inPredicate:
                return new InExpression(
                    ConvertExpression(inPredicate.Expression),
                    inPredicate.Values.Select(ConvertExpression).ToList(),
                    inPredicate.Subquery is { } subquery
                        ? new SubqueryExpression(ConvertQueryExpression(subquery.QueryExpression))
                        : null,
                    inPredicate.NotDefined);

            case Sdom.ExistsPredicate exists:
                return new ExistsExpression(new SubqueryExpression(
                    ConvertQueryExpression(exists.Subquery.QueryExpression)));

            case Sdom.LikePredicate like:
                return new LikeExpression(
                    ConvertExpression(like.FirstExpression),
                    ConvertExpression(like.SecondExpression),
                    like.EscapeExpression is { } escape ? ConvertExpression(escape) : null,
                    like.NotDefined);

            case Sdom.SubqueryComparisonPredicate:
                throw new SqlParseException(
                    "Subquery comparisons (for example = ANY (SELECT ...)) are not supported.");

            default:
                return ThrowUnsupported<SqlExpression>(expression);
        }
    }

    /// <summary>
    /// Maps a ScriptDom comparison type to the domain binary operator.
    /// </summary>
    private static SqlBinaryOperator MapComparisonOperator(Sdom.BooleanComparisonType type) =>
        type switch
        {
            Sdom.BooleanComparisonType.Equals => SqlBinaryOperator.Equal,
            Sdom.BooleanComparisonType.NotEqualToBrackets or Sdom.BooleanComparisonType.NotEqualToExclamation => SqlBinaryOperator.NotEqual,
            Sdom.BooleanComparisonType.GreaterThan => SqlBinaryOperator.GreaterThan,
            Sdom.BooleanComparisonType.LessThan => SqlBinaryOperator.LessThan,
            Sdom.BooleanComparisonType.GreaterThanOrEqualTo or Sdom.BooleanComparisonType.NotLessThan => SqlBinaryOperator.GreaterThanOrEqual,
            Sdom.BooleanComparisonType.LessThanOrEqualTo or Sdom.BooleanComparisonType.NotGreaterThan => SqlBinaryOperator.LessThanOrEqual,
            _ => throw new SqlParseException($"Unsupported comparison operator '{type}'.")
        };

    /// <summary>
    /// Rewrites BETWEEN / NOT BETWEEN into the equivalent pair of
    /// comparisons (the analysis model has no dedicated BETWEEN node).
    /// </summary>
    private SqlExpression ConvertBetween(Sdom.BooleanTernaryExpression between)
    {
        var low = ConvertExpression(between.SecondExpression);
        var high = ConvertExpression(between.ThirdExpression);

        if (between.TernaryExpressionType == Sdom.BooleanTernaryExpressionType.Between)
        {
            return new BinaryExpression(
                SqlBinaryOperator.And,
                new BinaryExpression(
                    SqlBinaryOperator.GreaterThanOrEqual,
                    ConvertExpression(between.FirstExpression),
                    low),
                new BinaryExpression(
                    SqlBinaryOperator.LessThanOrEqual,
                    ConvertExpression(between.FirstExpression),
                    high));
        }

        return new BinaryExpression(
            SqlBinaryOperator.Or,
            new BinaryExpression(
                SqlBinaryOperator.LessThan,
                ConvertExpression(between.FirstExpression),
                low),
            new BinaryExpression(
                SqlBinaryOperator.GreaterThan,
                ConvertExpression(between.FirstExpression),
                high));
    }

    /// <summary>
    /// Converts a scalar expression into the domain AST.
    /// </summary>
    private SqlExpression ConvertExpression(Sdom.ScalarExpression expression)
    {
        switch (expression)
        {
            case Sdom.ColumnReferenceExpression column:
                return ConvertColumnReference(column.MultiPartIdentifier);

            case Sdom.VariableReference variable:
                return new ParameterExpression(variable.Name);

            case Sdom.FunctionCall function:
                return ConvertFunctionCall(function);

            case Sdom.CastCall cast:
                return new CastExpression(
                    GetDataTypeText(cast.DataType),
                    GetDataTypeFamily(cast.DataType),
                    ConvertExpression(cast.Parameter),
                    CastKind.Cast);

            case Sdom.ConvertCall convert:
                return new CastExpression(
                    GetDataTypeText(convert.DataType),
                    GetDataTypeFamily(convert.DataType),
                    ConvertExpression(convert.Parameter),
                    CastKind.Convert);

            case Sdom.TryConvertCall tryConvert:
                return new CastExpression(
                    GetDataTypeText(tryConvert.DataType),
                    GetDataTypeFamily(tryConvert.DataType),
                    ConvertExpression(tryConvert.Parameter),
                    CastKind.TryConvert);

            case Sdom.SimpleCaseExpression simpleCase:
                return new CaseExpression(
                    simpleCase.InputExpression is { } input ? ConvertExpression(input) : null,
                    simpleCase.WhenClauses.Select(when => new CaseWhenClause(
                        ConvertExpression(when.WhenExpression),
                        ConvertExpression(when.ThenExpression))).ToList(),
                    simpleCase.ElseExpression is { } simpleElse ? ConvertExpression(simpleElse) : null);

            case Sdom.SearchedCaseExpression searchedCase:
                return new CaseExpression(
                    null,
                    searchedCase.WhenClauses.Select(when => new CaseWhenClause(
                        ConvertPredicate(when.WhenExpression),
                        ConvertExpression(when.ThenExpression))).ToList(),
                    searchedCase.ElseExpression is { } searchedElse ? ConvertExpression(searchedElse) : null);

            case Sdom.BinaryExpression binary:
                return new BinaryExpression(
                    MapArithmeticOperator(binary.BinaryExpressionType),
                    ConvertExpression(binary.FirstExpression),
                    ConvertExpression(binary.SecondExpression));

            case Sdom.UnaryExpression unary:
                return ConvertUnaryExpression(unary);

            case Sdom.CoalesceExpression coalesce:
                return new FunctionExpression(
                    "COALESCE",
                    coalesce.Expressions.Select(ConvertExpression).ToList());

            case Sdom.NullIfExpression nullIf:
                return new FunctionExpression(
                    "NULLIF",
                    [ConvertExpression(nullIf.FirstExpression), ConvertExpression(nullIf.SecondExpression)]);

            case Sdom.ScalarSubquery scalarSubquery:
                return new SubqueryExpression(ConvertQueryExpression(scalarSubquery.QueryExpression));

            case Sdom.Literal literal:
                return ConvertLiteral(literal);

            default:
                return ThrowUnsupported<SqlExpression>(expression);
        }
    }

    /// <summary>
    /// Converts a unary expression. <c>+x</c> is normalized to <c>x</c>.
    /// </summary>
    private SqlExpression ConvertUnaryExpression(Sdom.UnaryExpression unary)
    {
        if (unary.UnaryExpressionType == Sdom.UnaryExpressionType.Negative)
        {
            return new UnaryExpression(SqlUnaryOperator.Negate, ConvertExpression(unary.Expression));
        }

        if (unary.UnaryExpressionType == Sdom.UnaryExpressionType.Positive)
        {
            return ConvertExpression(unary.Expression);
        }

        throw new SqlParseException($"Unsupported unary operator '{unary.UnaryExpressionType}'.");
    }

    /// <summary>
    /// Maps a ScriptDom arithmetic operator to the domain binary operator.
    /// String concatenation (<c>+</c>) is the same operator as numeric
    /// addition in T-SQL and maps to <see cref="SqlBinaryOperator.Add"/>.
    /// </summary>
    private static SqlBinaryOperator MapArithmeticOperator(Sdom.BinaryExpressionType type) =>
        type switch
        {
            Sdom.BinaryExpressionType.Add or Sdom.BinaryExpressionType.Concat => SqlBinaryOperator.Add,
            Sdom.BinaryExpressionType.Subtract => SqlBinaryOperator.Subtract,
            Sdom.BinaryExpressionType.Multiply => SqlBinaryOperator.Multiply,
            Sdom.BinaryExpressionType.Divide => SqlBinaryOperator.Divide,
            _ => throw new SqlParseException($"Unsupported arithmetic operator '{type}'.")
        };

    /// <summary>
    /// Converts a column reference. <c>table.column</c>,
    /// <c>schema.table.column</c> and <c>server.database.schema.column</c>
    /// all collapse to (name, table) in the analysis model.
    /// </summary>
    private static ColumnExpression ConvertColumnReference(Sdom.MultiPartIdentifier identifier)
    {
        if (identifier is null || identifier.Count == 0)
        {
            throw new SqlParseException(
                "Unexpected column reference without an identifier (only valid as COUNT(*)).");
        }

        if (identifier.Count <= 1)
        {
            return new ColumnExpression(identifier.Identifiers[0].Value);
        }

        return new ColumnExpression(identifier.Identifiers[^1].Value, identifier.Identifiers[^2].Value);
    }

    /// <summary>
    /// Converts a function call into an aggregate expression (when the
    /// function is a built-in aggregate) or a scalar function expression.
    /// </summary>
    private SqlExpression ConvertFunctionCall(Sdom.FunctionCall function)
    {
        var name = function.FunctionName?.Value ?? string.Empty;
        var window = function.OverClause is { } overClause ? ConvertOverClause(overClause) : null;
        var distinct = function.UniqueRowFilter == Sdom.UniqueRowFilter.Distinct;

        if (function.WithinGroupClause is not null)
        {
            AddWarning($"WITHIN GROUP clause on '{name}' is not represented and was ignored.");
        }

        if (!AggregateFunctionNames.Contains(name))
        {
            return new FunctionExpression(
                name,
                function.Parameters.Select(ConvertExpression).ToList(),
                window);
        }

        if (IsCountStarArgument(function))
        {
            return new AggregateExpression(name, [], false, true, window);
        }

        return new AggregateExpression(
            name,
            function.Parameters.Select(ConvertExpression).ToList(),
            distinct,
            false,
            window);
    }

    /// <summary>
    /// True when the single argument of the function call is the <c>*</c>
    /// form used by COUNT(*).
    /// </summary>
    private static bool IsCountStarArgument(Sdom.FunctionCall function) =>
        function.Parameters.Count == 1 &&
        function.Parameters[0] is Sdom.ColumnReferenceExpression column &&
        IsStarColumn(column);

    /// <summary>
    /// True when the column reference is the <c>*</c> marker used by
    /// COUNT(*). ScriptDom may represent it either as a one-part
    /// identifier named <c>*</c> or as a column reference without an
    /// identifier at all.
    /// </summary>
    private static bool IsStarColumn(Sdom.ColumnReferenceExpression column) =>
        column.MultiPartIdentifier is null ||
        (column.MultiPartIdentifier.Identifiers.Count == 1 &&
         string.Equals(column.MultiPartIdentifier.Identifiers[0].Value, "*", StringComparison.Ordinal));

    /// <summary>
    /// Converts an OVER clause into a window function node.
    /// </summary>
    private WindowFunction ConvertOverClause(Sdom.OverClause overClause)
    {
        var partitions = overClause.Partitions?.Select(ConvertExpression).ToList() ?? [];
        var orderBy = overClause.OrderByClause?.OrderByElements is { Count: > 0 } elements
            ? elements.Select(e => new OrderByItem(
                ConvertExpression(e.Expression),
                e.SortOrder != Sdom.SortOrder.Descending)).ToList()
            : [];

        if (overClause.WindowName is not null)
        {
            AddWarning($"Named window '{overClause.WindowName.Value}' is not represented; its frame was inlined.");
        }

        return new WindowFunction(partitions, orderBy, RenderWindowFrame(overClause.WindowFrameClause));
    }

    /// <summary>
    /// Renders a window frame clause as canonical text.
    /// </summary>
    private static string? RenderWindowFrame(Sdom.WindowFrameClause? frame)
    {
        if (frame is null)
        {
            return null;
        }

        var type = frame.WindowFrameType == Sdom.WindowFrameType.Rows ? "ROWS" : "RANGE";
        return $"{type} BETWEEN {RenderWindowDelimiter(frame.Top)} AND {RenderWindowDelimiter(frame.Bottom)}";
    }

    /// <summary>
    /// Renders a window frame delimiter as canonical text.
    /// </summary>
    private static string RenderWindowDelimiter(Sdom.WindowDelimiter delimiter) =>
        delimiter.WindowDelimiterType switch
        {
            Sdom.WindowDelimiterType.UnboundedPreceding => "UNBOUNDED PRECEDING",
            Sdom.WindowDelimiterType.CurrentRow => "CURRENT ROW",
            Sdom.WindowDelimiterType.UnboundedFollowing => "UNBOUNDED FOLLOWING",
            Sdom.WindowDelimiterType.ValuePreceding => $"{FormatWindowOffset(delimiter.OffsetValue)} PRECEDING",
            Sdom.WindowDelimiterType.ValueFollowing => $"{FormatWindowOffset(delimiter.OffsetValue)} FOLLOWING",
            _ => delimiter.WindowDelimiterType.ToString()
        };

    /// <summary>
    /// Formats the numeric offset of a <c>N PRECEDING/FOLLOWING</c> delimiter.
    /// </summary>
    private static string FormatWindowOffset(Sdom.ScalarExpression? offset) =>
        offset switch
        {
            null => "?",
            Sdom.Literal literal => literal.Value,
            Sdom.VariableReference variable => variable.Name,
            Sdom.ColumnReferenceExpression column => column.MultiPartIdentifier.Identifiers[^1].Value,
            _ => offset.GetType().Name
        };

    /// <summary>
    /// Converts a literal. The value is preserved as source text and the
    /// apparent type family is derived from the literal kind.
    /// </summary>
    private static SqlExpression ConvertLiteral(Sdom.Literal literal)
    {
        switch (literal)
        {
            case Sdom.IntegerLiteral:
                return new LiteralExpression(literal.Value, "int");

            case Sdom.NumericLiteral:
                return new LiteralExpression(literal.Value, "decimal");

            case Sdom.RealLiteral:
                return new LiteralExpression(literal.Value, "float");

            case Sdom.MoneyLiteral:
                return new LiteralExpression(literal.Value, "money");

            case Sdom.BinaryLiteral:
                return new LiteralExpression(literal.Value, "varbinary");

            case Sdom.StringLiteral stringLiteral:
                return new LiteralExpression(stringLiteral.Value, stringLiteral.IsNational ? "nvarchar" : "varchar");

            case Sdom.NullLiteral:
                return new LiteralExpression("NULL", "null");

            case Sdom.DefaultLiteral:
                return new LiteralExpression("DEFAULT", "default");

            default:
                return new LiteralExpression(literal.Value, literal.LiteralType.ToString().ToLowerInvariant());
        }
    }

    /// <summary>
    /// Renders a data type reference as text (for example <c>NVARCHAR(100)</c>).
    /// </summary>
    private static string GetDataTypeText(Sdom.DataTypeReference dataType)
    {
        var name = dataType.Name?.BaseIdentifier?.Value ?? string.Empty;

        if (dataType is Sdom.ParameterizedDataTypeReference { Parameters.Count: > 0 } parameterized)
        {
            var parameters = string.Join(", ", parameterized.Parameters.Select(p => p.Value));
            return name.Length > 0 ? $"{name}({parameters})" : parameters;
        }

        return name;
    }

    /// <summary>
    /// Normalized (lower case) type family of a data type reference.
    /// </summary>
    private static string GetDataTypeFamily(Sdom.DataTypeReference dataType) =>
        dataType.Name?.BaseIdentifier?.Value?.ToLowerInvariant() ?? string.Empty;

    /// <summary>
    /// Records a non-fatal warning (duplicates are dropped).
    /// </summary>
    private void AddWarning(string warning)
    {
        if (!_warnings.Contains(warning))
        {
            _warnings.Add(warning);
        }
    }

    /// <summary>
    /// Throws for a ScriptDom node the analysis model cannot represent.
    /// </summary>
    private static T ThrowUnsupported<T>(Sdom.TSqlFragment fragment)
        where T : class
    {
        throw new SqlParseException(
            $"The SQL construct '{fragment.GetType().Name}' cannot be represented in the SqlOptimizer AST.");
    }
}