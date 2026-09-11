using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;

namespace SqlOptimizer.Rules.Support;

/// <summary>
/// Helpers to enumerate the predicate positions of a single statement
/// (WHERE, HAVING and the ON predicates of the joins directly contained in
/// its FROM clause; joins inside derived tables belong to the inner
/// statement), unwrap NOT, and extract the operand expressions of a
/// predicate. Shared by the predicate based rules to keep traversal logic in
/// one place.
/// </summary>
public static class PredicatePositions
{
    /// <summary>
    /// Enumerates the top-level predicate positions of a statement: WHERE,
    /// HAVING and the ON predicates of the joins directly contained in its
    /// FROM clause.
    /// </summary>
    /// <param name="statement">The statement to inspect.</param>
    public static IEnumerable<SqlExpression> DirectPredicates(SelectStatement statement)
    {
        if (statement.Where is not null)
        {
            yield return statement.Where;
        }

        if (statement.Having is not null)
        {
            yield return statement.Having;
        }

        if (statement.From is not null)
        {
            foreach (var join in JoinFinder.FindJoins(statement.From.Source, includeSubqueries: false))
            {
                if (join.Predicate is not null)
                {
                    yield return join.Predicate;
                }
            }
        }
    }

    /// <summary>Strips a single NOT wrapper from a predicate.</summary>
    /// <param name="expression">The predicate to unwrap.</param>
    public static SqlExpression UnwrapNot(SqlExpression expression) =>
        expression is UnaryExpression { Operator: SqlUnaryOperator.Not } unary
            ? unary.Operand
            : expression;

    /// <summary>
    /// Enumerates the operand expressions of a predicate: both sides of a
    /// comparison (or IS/IS NOT NULL), the LIKE operand or the IN operand.
    /// </summary>
    /// <param name="predicate">The predicate to decompose.</param>
    public static IEnumerable<SqlExpression> OperandExpressions(SqlExpression predicate)
    {
        switch (predicate)
        {
            case BinaryExpression binary
                when binary.IsComparison || binary.Operator is SqlBinaryOperator.Is or SqlBinaryOperator.IsNot:
                yield return binary.Left;
                yield return binary.Right;
                break;

            case LikeExpression like:
                yield return like.Expression;
                break;

            case InExpression inExpression:
                yield return inExpression.Expression;
                break;
        }
    }

    /// <summary>
    /// True when the operand is a scalar function (not windowed) or a
    /// CAST/CONVERT/TRY_CONVERT that contains a column reference. Reports the
    /// function description and the first wrapped column.
    /// </summary>
    /// <param name="operand">The predicate operand to inspect.</param>
    /// <param name="functionDescription">Function/cast description.</param>
    /// <param name="column">The first column wrapped by the function/cast.</param>
    public static bool TryGetWrappedColumn(
        SqlExpression operand,
        out string functionDescription,
        out ColumnExpression? column)
    {
        functionDescription = string.Empty;
        column = null;

        if (operand is FunctionExpression { IsWindowed: false } function
            && function.Arguments.Count > 0
            && RuleUtilities.ReferencesColumn(function))
        {
            functionDescription = function.Name;
            column = SqlAstWalker.OfType<ColumnExpression>(function).FirstOrDefault();
            return column is not null;
        }

        if (operand is CastExpression cast && RuleUtilities.ReferencesColumn(cast))
        {
            functionDescription = cast.Kind switch
            {
                CastKind.Convert => $"CONVERT AS {cast.TargetType}",
                CastKind.TryConvert => $"TRY_CONVERT AS {cast.TargetType}",
                _ => $"CAST AS {cast.TargetType}"
            };

            column = SqlAstWalker.OfType<ColumnExpression>(cast).FirstOrDefault();
            return column is not null;
        }

        return false;
    }

    /// <summary>Renders an expression as a deterministic SQL fragment.</summary>
    /// <param name="expression">The expression to render.</param>
    public static string Describe(SqlExpression expression) =>
        ExpressionNormalizer.Normalize(expression);
}