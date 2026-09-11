using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// One item of the SELECT list. Either an expression (with optional alias) or
/// a star projection (<c>*</c> or <c>t.*</c>).
/// </summary>
/// <param name="Expression">Projected expression. Null for star projections.</param>
/// <param name="Alias">Output alias when present.</param>
/// <param name="IsStar">True for <c>*</c> or <c>t.*</c>.</param>
/// <param name="StarTableAlias">Table alias for <c>t.*</c>, null for plain <c>*</c>.</param>
public sealed record SelectItem(
    SqlExpression? Expression,
    string? Alias = null,
    bool IsStar = false,
    string? StarTableAlias = null) : SqlNode
{
    /// <summary>Creates a normal projected expression item.</summary>
    /// <param name="expression">The projected expression.</param>
    /// <param name="alias">Optional alias.</param>
    public SelectItem(SqlExpression expression, string? alias = null)
        : this(expression, alias, false, null)
    {
    }

    /// <summary>Creates a star projection item.</summary>
    /// <param name="tableAlias">Table alias for <c>t.*</c>, or null for plain <c>*</c>.</param>
    public static SelectItem Star(string? tableAlias = null) =>
        new(null, null, true, tableAlias);

    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children
    {
        get
        {
            if (Expression is not null)
            {
                yield return Expression;
            }
        }
    }
}
