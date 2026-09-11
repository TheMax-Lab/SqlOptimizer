using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Common;

namespace SqlOptimizer.Domain.Parsing;

/// <summary>
/// Result of parsing a SQL statement into the SqlOptimizer AST.
/// </summary>
public sealed record ParsedQuery
{
    /// <summary>Creates a new parsing result.</summary>
    /// <param name="root">The parsed SELECT statement.</param>
    /// <param name="warnings">Non-fatal parser warnings; defaults to none.</param>
    public ParsedQuery(SelectStatement root, IReadOnlyList<string>? warnings = null)
    {
        Root = root;
        Warnings = warnings ?? [];
    }

    /// <summary>The parsed SELECT statement (with CTEs and set operations attached).</summary>
    public SelectStatement Root { get; init; }

    /// <summary>Non-fatal parser warnings (for example ignored statements).</summary>
    public IReadOnlyList<string> Warnings { get; init; }
}
