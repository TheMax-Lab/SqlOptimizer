using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// A set operation (UNION / UNION ALL / INTERSECT / EXCEPT) chaining two
/// SELECT statements. A statement may carry at most one set operation; the
/// right-hand side may itself carry further set operations.
/// </summary>
/// <param name="Operator">Set operator kind.</param>
/// <param name="Right">Right-hand SELECT statement.</param>
public sealed record SetOperation(SetOperatorKind Operator, SelectStatement Right) : SqlNode
{
    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children
    {
        get
        {
            yield return Right;
        }
    }
}
