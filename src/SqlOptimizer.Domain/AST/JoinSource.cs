using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// A join between two FROM sources. Nested joins (A JOIN B JOIN C) form a
/// left-deep tree of <see cref="JoinSource"/> nodes.
/// </summary>
/// <param name="Left">Left (outer) source.</param>
/// <param name="Right">Right (inner) source.</param>
/// <param name="Type">Join type.</param>
/// <param name="Predicate">ON predicate. Null for CROSS JOIN; a constant predicate
/// (for example <c>ON 1 = 1</c>) is preserved as-is so rules can detect it.</param>
public sealed record JoinSource(
    FromSource Left,
    FromSource Right,
    JoinType Type,
    SqlExpression? Predicate) : FromSource
{
    /// <summary>
    /// True when the join carries a meaningful (non-constant) ON predicate.
    /// </summary>
    public bool HasMeaningfulPredicate => Predicate is { } predicate && !IsConstantPredicate(predicate);

    /// <summary>
    /// Determines whether a predicate is effectively constant (for example
    /// <c>1 = 1</c>) and therefore does not relate the joined tables.
    /// </summary>
    /// <param name="predicate">The predicate to inspect.</param>
    public static bool IsConstantPredicate(SqlExpression predicate)
    {
        if (predicate is LiteralExpression)
        {
            return true;
        }

        return predicate is BinaryExpression { Left: LiteralExpression, Right: LiteralExpression };
    }

    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children
    {
        get
        {
            yield return Left;
            yield return Right;

            if (Predicate is not null)
            {
                yield return Predicate;
            }
        }
    }
}
