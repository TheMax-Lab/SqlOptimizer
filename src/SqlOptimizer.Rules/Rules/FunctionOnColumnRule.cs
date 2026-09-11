using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.AST.Visitors;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Rules.Support;

namespace SqlOptimizer.Rules;

/// <summary>
/// SQL002 — a scalar function or CAST/CONVERT applied to a column in a
/// predicate (for example <c>YEAR(OrderDate) = 2025</c>,
/// <c>LOWER(Name) = 'john'</c>). Such predicates usually cannot use an index
/// on the raw column. Aggregates, windowed functions and functions without
/// column arguments (for example <c>GETDATE()</c>) are not reported.
/// </summary>
public sealed class FunctionOnColumnRule : SqlOptimizationRuleBase
{
    /// <inheritdoc />
    public override string Id => "SQL002";

    /// <inheritdoc />
    public override string Name => "Function applied to a column in a predicate";

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
                        if (!PredicatePositions.TryGetWrappedColumn(operand, out var functionDescription, out var column))
                        {
                            continue;
                        }

                        var resolved = column is not null
                            ? RuleUtilities.ResolveColumn(context, statement, column)
                            : null;

                        yield return CreateFinding(
                            Severity.High,
                            FindingCategory.Sargability,
                            $"Function {functionDescription} is applied to column '{Describe(column)}' in a predicate.",
                            PredicatePositions.Describe(operand),
                            explanation: "A predicate on a transformed value of a column usually cannot use an index on the raw column, because the index stores untransformed values. Whether this actually hurts depends on table size, index design and selectivity. Where semantics allow, prefer a predicate on the raw column (for example a date range instead of a year comparison) or a persisted computed column with an index; verify the effect with an execution plan.",
                            recommendations: new[]
                            {
                                "Rewrite the predicate on the raw column when the semantics allow (for example replace YEAR(OrderDate) = 2025 with a date range).",
                                "Consider a persisted computed column with an index when the transformed value is used repeatedly.",
                                "Check the execution plan for a scan caused by the predicate before changing the query."
                            },
                            confidence: resolved is not null ? 0.9 : 0.75,
                            impact: new OptimizationImpact(Performance: 6, Readability: 0, Maintainability: 1, Risk: 2));
                    }
                }
            }
        }
    }

    /// <summary>Formats a column for messages.</summary>
    /// <param name="column">The column reference.</param>
    private static string Describe(ColumnExpression? column) =>
        column is null ? "column" : column.TableAlias is null ? column.Name : $"{column.TableAlias}.{column.Name}";
}