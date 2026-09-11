using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// A scalar subquery used as an expression, for example
/// <c>WHERE Total = (SELECT MAX(Total) FROM Orders)</c>.
/// </summary>
/// <param name="Statement">The inner SELECT statement.</param>
/// <param name="Alias">Alias when the subquery is aliased.</param>
public sealed record SubqueryExpression(SelectStatement Statement, string? Alias = null) : SqlExpression
{
    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children
    {
        get
        {
            yield return Statement;
        }
    }
}
