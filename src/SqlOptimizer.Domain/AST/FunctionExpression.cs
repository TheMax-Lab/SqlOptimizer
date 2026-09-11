using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// A scalar function invocation (for example <c>LOWER(Name)</c>,
/// <c>GETDATE()</c>), optionally followed by a window clause
/// (for example <c>ROW_NUMBER() OVER (...)</c>).
/// </summary>
/// <param name="Name">Function name.</param>
/// <param name="Arguments">Positional arguments.</param>
/// <param name="Window">Window clause when the function is used as a window function.</param>
public sealed record FunctionExpression(
    string Name,
    IReadOnlyList<SqlExpression> Arguments,
    WindowFunction? Window = null) : SqlExpression
{
    /// <summary>True when the function is used with an OVER clause.</summary>
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
