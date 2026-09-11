using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// A Common Table Expression (WITH clause) attached to a SELECT statement.
/// </summary>
/// <param name="Name">CTE name.</param>
/// <param name="Columns">Explicit column list when present.</param>
/// <param name="Query">The CTE SELECT statement.</param>
/// <param name="IsRecursive">True when defined inside a recursive CTE.</param>
public sealed record CommonTableExpression(
    string Name,
    IReadOnlyList<string> Columns,
    SelectStatement Query,
    bool IsRecursive = false) : SqlNode
{
    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children
    {
        get
        {
            yield return Query;
        }
    }
}
