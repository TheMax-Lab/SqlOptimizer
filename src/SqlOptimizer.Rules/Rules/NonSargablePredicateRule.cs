using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Support;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL003 — non-sargable predicate: arithmetic (or negation) applied to
/// columns inside a predicate, for example <c>Price * Quantity &gt; 100</c>.
/// Function and CAST wrappers are the responsibility of SQL002, so this rule
/// skips operands that already contain a function or cast to avoid
/// duplicate findings.
/// </summary>
public sealed class NonSargablePredicateRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL003";

    /// <inheritdoc />
    public override string Name => "Non-sargable predicate";

    /// <inheritdoc />
    public override IEnumerable<SqlFinding> Analyze(SqlAnalysisContext context)
    {
        foreach (var statement in RuleUtilities.EnumerateStatements(context.Ast))
        {
            foreach (var predicate in PredicatePositions.DirectPredicates(statement))
            {
                foreach (var atomic in SqlExpressionFinder.SplitPredicates(predicate))
                {
                    var unwrapped = PredicatePositions.UnwrapNot(atomic);

                    foreach (var operand in PredicatePositions.OperandExpressions(unwrapped))
                    {
                        if (TryDescribeArithmetic(operand, out var columns, out var description, out var confidence))
                        {
                            yield return CreateFinding(
                                Severity.High,
                                FindingCategory.Sargability,
                                $"Arithmetic on column(s) ({columns}) in a predicate prevents index seeks.",
                                PredicatePositions.Describe(operand),
                                explanation: $"The predicate compares an expression built from columns ({description}) to a value. The optimizer usually cannot satisfy this with a seek on an individual column. Some rewrites (for example moving the arithmetic to the constant side of an equality) are only valid under specific conditions (non-zero divisor, overflow behavior), so no automatic rewrite is applied.",
                                recommendations: new[]
                                {
                                    "If safe for the operator, move the arithmetic to the constant side of the comparison.",
                                    "Consider a persisted computed column with an index when the expression is used repeatedly.",
                                    "Check the execution plan for a scan caused by the predicate."
                                },
                                confidence: confidence,
                                impact: new OptimizationImpact(Performance: 6, Readability: 0, Maintainability: 1, Risk: 2));
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// True when the operand is arithmetic (or negation) over columns and
    /// does not already contain a function or cast (SQL002 territory).
    /// </summary>
    private static bool TryDescribeArithmetic(
        SqlExpression operand,
        out string columns,
        out string description,
        out double confidence)
    {
        columns = string.Empty;
        description = string.Empty;
        confidence = 0.0;

        if (operand is BinaryExpression { IsArithmetic: true } arithmetic)
        {
            if (RuleUtilities.ReferencesColumn(arithmetic)
                && !RuleUtilities.UsesFunctionOrCast(arithmetic))
            {
                columns = FormatColumns(arithmetic);
                description = PredicatePositions.Describe(arithmetic);
                confidence = 0.8;
                return true;
            }
        }

        if (operand is UnaryExpression { Operator: SqlUnaryOperator.Negate } negation)
        {
            if (RuleUtilities.ReferencesColumn(negation.Operand)
                && !RuleUtilities.UsesFunctionOrCast(negation.Operand))
            {
                columns = FormatColumns(negation.Operand);
                description = PredicatePositions.Describe(negation.Operand);
                confidence = 0.6;
                return true;
            }
        }

        return false;
    }

    /// <summary>Formats distinct column names deterministically.</summary>
    private static string FormatColumns(SqlExpression expression) =>
        string.Join(
            ", ",
            SqlAstWalker.OfType<ColumnExpression>(expression)
                .Select(c => c.TableAlias is null ? c.Name : $"{c.TableAlias}.{c.Name}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.Ordinal));
}