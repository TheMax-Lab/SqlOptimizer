namespace SqlOptimizer.Domain.Metadata;

/// <summary>
/// Describes a database index.
/// </summary>
/// <param name="Name">Index name.</param>
/// <param name="KeyColumns">Key columns in index order.</param>
/// <param name="IncludedColumns">Covering (INCLUDE) columns.</param>
/// <param name="Unique">True when the index is unique.</param>
/// <param name="Clustered">True when the index is clustered.</param>
public sealed record DatabaseIndex(
    string Name,
    IReadOnlyList<string> KeyColumns,
    IReadOnlyList<string> IncludedColumns,
    bool Unique,
    bool Clustered);
