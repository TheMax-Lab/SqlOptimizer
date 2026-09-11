using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// A parameter placeholder (for example <c>@p1</c>).
/// </summary>
/// <param name="Name">Parameter name including the leading @.</param>
public sealed record ParameterExpression(string Name) : SqlExpression
{
    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children => [];
}
