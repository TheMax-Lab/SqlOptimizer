using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// A derived table (inline view) in a FROM clause:
/// <c>FROM (SELECT ...) AS x</c>.
/// </summary>
/// <param name="Query">The inner SELECT statement.</param>
/// <param name="Alias">Mandatory alias of the derived table.</param>
public sealed record SubquerySource(SelectStatement Query, string? Alias) : FromSource
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
