using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// Reference to a column, optionally qualified with a table alias
/// (for example <c>c.Name</c>).
/// </summary>
/// <param name="Name">Column name.</param>
/// <param name="TableAlias">Table alias when the column is qualified, otherwise <c>null</c>.</param>
public sealed record ColumnExpression(string Name, string? TableAlias = null) : SqlExpression
{
    /// <summary>True when the column reference is qualified with a table alias.</summary>
    public bool IsQualified => TableAlias is not null;

    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children => [];
}
