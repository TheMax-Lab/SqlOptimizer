using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// One WHEN/THEN clause of a <see cref="CaseExpression"/>.
/// </summary>
/// <param name="When">Condition expression.</param>
/// <param name="Then">Result expression.</param>
public sealed record CaseWhenClause(SqlExpression When, SqlExpression Then) : SqlNode
{
    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children
    {
        get
        {
            yield return When;
            yield return Then;
        }
    }
}
