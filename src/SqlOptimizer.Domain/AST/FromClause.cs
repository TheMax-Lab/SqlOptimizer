using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// The FROM clause of a SELECT statement. The source is either a single
/// table/subquery or a (possibly nested) tree of joins.
/// </summary>
/// <param name="Source">The FROM source.</param>
public sealed record FromClause(FromSource Source) : SqlNode
{
    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children
    {
        get
        {
            yield return Source;
        }
    }
}
