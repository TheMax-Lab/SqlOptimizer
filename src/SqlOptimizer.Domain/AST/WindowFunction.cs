using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// An OVER clause attached to a window function or windowed aggregate:
/// <c>OVER (PARTITION BY ... ORDER BY ...)</c>.
/// </summary>
/// <param name="PartitionBy">PARTITION BY expressions.</param>
/// <param name="OrderBy">ORDER BY items.</param>
/// <param name="FrameText">Frame specification text when present (for example <c>ROWS BETWEEN ...</c>).</param>
public sealed record WindowFunction(
    IReadOnlyList<SqlExpression> PartitionBy,
    IReadOnlyList<OrderByItem> OrderBy,
    string? FrameText = null) : SqlNode
{
    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children
    {
        get
        {
            foreach (var partition in PartitionBy)
            {
                yield return partition;
            }

            foreach (var order in OrderBy)
            {
                yield return order;
            }
        }
    }
}
