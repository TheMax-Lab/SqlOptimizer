using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// An aggregate function invocation (for example <c>COUNT(*)</c>,
/// <c>MAX(Quantity)</c>), optionally used as a windowed aggregate.
/// </summary>
/// <param name="FunctionName">Aggregate function name (COUNT, SUM, AVG, MIN, MAX, ...).</param>
/// <param name="Arguments">Arguments. Empty for <c>COUNT(*)</c> when <see cref="CountStar"/> is set.</param>
/// <param name="Distinct">True when DISTINCT is used inside the aggregate.</param>
/// <param name="CountStar">True for the special <c>COUNT(*)</c> form.</param>
/// <param name="Window">Window clause when the aggregate is used as a window function.</param>
public sealed record AggregateExpression(
    string FunctionName,
    IReadOnlyList<SqlExpression> Arguments,
    bool Distinct = false,
    bool CountStar = false,
    WindowFunction? Window = null) : SqlExpression
{
    /// <summary>True when the aggregate is used with an OVER clause.</summary>
    public bool IsWindowed => Window is not null;

    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children
    {
        get
        {
            foreach (var argument in Arguments)
            {
                yield return argument;
            }

            if (Window is not null)
            {
                yield return Window;
            }
        }
    }
}
