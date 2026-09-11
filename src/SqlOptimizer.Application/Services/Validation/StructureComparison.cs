using SqlOptimizer.Application.DTOs;

namespace SqlOptimizer.Application.Services.Validation;

/// <summary>
/// Outcome of the structural comparison between the original and candidate
/// statements. The comparison distinguishes provable structural differences
/// (the result contract or structure demonstrably changed) from unverified
/// differences (structure changed in a way that may or may not preserve
/// semantics, and cannot be decided from the AST alone). It never claims
/// semantic equivalence: verified facts are structural facts only.
/// </summary>
public sealed record StructureComparison
{
    /// <summary>Differences that demonstrably change behavior or the result contract.</summary>
    public required IReadOnlyList<string> ProvableDifferences { get; init; }

    /// <summary>Differences whose semantic effect cannot be decided from the AST alone.</summary>
    public required IReadOnlyList<string> UnverifiedDifferences { get; init; }

    /// <summary>Structural facts positively verified as identical.</summary>
    public required IReadOnlyList<string> VerifiedFacts { get; init; }

    /// <summary>Semantic risk flags typed by the structural comparison.</summary>
    public required IReadOnlyList<SemanticRisk> Risks { get; init; }

    /// <summary>Tables referenced by either statement.</summary>
    public required IReadOnlyList<string> AffectedObjects { get; init; }
}
