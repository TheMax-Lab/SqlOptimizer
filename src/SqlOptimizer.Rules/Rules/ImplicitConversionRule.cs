using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Support;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL005 — implicit conversion risk in a comparison between a column and a
/// string/numeric literal (for example <c>varchar_id = 42</c>). Requires
/// schema metadata: without column types the rule cannot decide and is
/// skipped. Parameters are ignored because their types are not part of the
/// AST.
/// </summary>
public sealed class ImplicitConversionRule : SqlOptimizationRuleBase
{
    private const string Char = "char";
    private const string Int = "int";
    private const string Decimal = "decimal";
    private const string Float = "float";
    private const string Bit = "bit";
    private const string Date = "date";
    private const string Datetime = "datetime";

    /// <inheritdoc />
    public override string Id => "SQL005";

    /// <inheritdoc />
    public override string Name => "Implicit conversion in comparison";

    /// <inheritdoc />
    public override bool CanAnalyze(SqlAnalysisContext context) => context.Schema is not null;

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        foreach (var statement in RuleUtilities.EnumerateStatements(context.Ast))
        {
            foreach (var predicate in PredicatePositions.DirectPredicates(statement))
            {
                foreach (var atomic in SqlExpressionFinder.SplitPredicates(predicate))
                {
                    if (atomic is not BinaryExpression { IsComparison: true } comparison)
                    {
                        continue;
                    }

                    var pair = GetColumnAndLiteral(comparison.Left, comparison.Right)
                        ?? GetColumnAndLiteral(comparison.Right, comparison.Left);

                    if (pair is null)
                    {
                        continue;
                    }

                    var (column, literal) = pair.Value;

                    var resolved = RuleUtilities.ResolveColumn(context, statement, column);
                    if (resolved is null)
                    {
                        continue;
                    }

                    var columnFamily = RuleUtilities.GetTypeFamily(resolved.DataType);
                    var literalFamily = RuleUtilities.GetTypeFamily(literal.DataType);

                    if (!IsRiskyConversion(columnFamily, literalFamily, out var confidence, out var detail))
                    {
                        continue;
                    }

                    yield return CreateFinding(
                        Severity.Warning,
                        FindingCategory.Sargability,
                        $"Comparison of {columnFamily} column '{column.Name}' with a {literalFamily} literal may cause an implicit conversion.",
                        $"{PredicatePositions.Describe(comparison.Left)} {DescribeOperator(comparison.Operator)} {PredicatePositions.Describe(comparison.Right)}",
                        explanation: $"SQL Server converts one side to the other data type before comparing ({detail}). When the column side is converted, an index on the raw column usually cannot be used for a seek. Confidence is based on the known column type '{resolved.DataType}'; the actual conversion behavior depends on collation and type precedence.",
                        recommendations: new[]
                        {
                            "Compare values using the column's own data type (for example a date literal for a date column).",
                            "If the source data uses a different format, convert it once upstream (ETL) or use a computed column.",
                            "Verify with an execution plan whether the predicate causes a scan."
                        },
                        confidence: confidence,
                        impact: new OptimizationImpact(Performance: 5, Readability: 0, Maintainability: 1, Risk: 2));
                }
            }
        }
    }

    /// <summary>
    /// Detects the (column, literal) pair of a comparison, when one side is a
    /// column and the other a non-null literal.
    /// </summary>
    private static (ColumnExpression Column, LiteralExpression Literal)? GetColumnAndLiteral(
        SqlExpression left, SqlExpression right)
    {
        if (left is ColumnExpression column && right is LiteralExpression { IsNull: false } literal)
        {
            return (column, literal);
        }

        return null;
    }

    /// <summary>
    /// Decides whether the type combination is a risky implicit conversion.
    /// </summary>
    private static bool IsRiskyConversion(
        string? columnFamily,
        string? literalFamily,
        out double confidence,
        out string detail)
    {
        confidence = 0.0;
        detail = string.Empty;

        var isCharPair = columnFamily == Char || literalFamily == Char;
        var isNumericPair = columnFamily is Int or Decimal or Float or Bit
            || literalFamily is Int or Decimal or Float or Bit;
        var isTemporalPair = columnFamily is Date or Datetime
            || literalFamily is Date or Datetime;

        if (isCharPair && isNumericPair)
        {
            confidence = 0.85;
            detail = "character values are converted to the numeric type of the column";
            return true;
        }

        if (isCharPair && isTemporalPair)
        {
            confidence = 0.8;
            detail = "the character literal is parsed as a date/time value using the current date format";
            return true;
        }

        if (isTemporalPair && isNumericPair)
        {
            confidence = 0.75;
            detail = "numeric values are converted to the date/time type (for example 20250101)";
            return true;
        }

        if ((columnFamily == Int && literalFamily == Decimal)
            || (columnFamily == Decimal && literalFamily == Int))
        {
            confidence = 0.6;
            detail = "the integer column is converted to decimal to compare with a decimal literal";
            return true;
        }

        if ((columnFamily == Int && literalFamily == Float)
            || (columnFamily == Float && literalFamily == Int)
            || (columnFamily == Decimal && literalFamily == Float)
            || (columnFamily == Float && literalFamily == Decimal))
        {
            confidence = 0.55;
            detail = "the column is converted to the floating point literal type";
            return true;
        }

        return false;
    }

    /// <summary>Formats a comparison operator for messages.</summary>
    /// <param name="op">The comparison operator.</param>
    private static string DescribeOperator(SqlBinaryOperator op) => op switch
    {
        SqlBinaryOperator.Equal => "=",
        SqlBinaryOperator.NotEqual => "<>",
        SqlBinaryOperator.GreaterThan => ">",
        SqlBinaryOperator.GreaterThanOrEqual => ">=",
        SqlBinaryOperator.LessThan => "<",
        SqlBinaryOperator.LessThanOrEqual => "<=",
        _ => op.ToString()
    };
}