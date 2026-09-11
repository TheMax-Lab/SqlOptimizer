using SqlOptimizer.Domain.AST;

namespace SqlOptimizer.Application.Services.Validation;

/// <summary>
/// Structural projection of one item of the SELECT list, as captured by
/// <see cref="QueryStructureSnapshot"/>.
/// </summary>
/// <param name="NormalizedExpression">Canonical expression text; empty for star projections.</param>
/// <param name="Alias">Output alias when present.</param>
/// <param name="IsStar">True for <c>*</c> or <c>t.*</c> projections.</param>
/// <param name="StarTableAlias">Table alias for <c>t.*</c>; null for plain <c>*</c>.</param>
/// <param name="Expression">The AST expression; null for star projections.</param>
public sealed record ProjectionStructure(
    string NormalizedExpression,
    string? Alias,
    bool IsStar,
    string? StarTableAlias,
    SqlExpression? Expression)
{
}
