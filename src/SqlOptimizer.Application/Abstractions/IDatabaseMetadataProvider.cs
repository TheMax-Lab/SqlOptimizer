using SqlOptimizer.Domain.Metadata;

namespace SqlOptimizer.Application.Abstractions;

/// <summary>
/// Focused contract for reading live schema metadata (tables, columns,
/// data types, nullability, primary keys, indexes, row-count estimates)
/// from a database. Kept separate from <see cref="ISqlDatabaseProvider"/>
/// because the validation flow only needs metadata, not plan/execution
/// capabilities. Implementations must be read-only, must never fabricate
/// metadata and must return null instead of throwing for normal
/// operational failures (unconfigured, connection or query errors).
/// </summary>
public interface IDatabaseMetadataProvider
{
    /// <summary>True when a live metadata source is configured and usable.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Reads schema metadata for the given tables. Names may be qualified
    /// (for example <c>dbo.Orders</c>) or unqualified. Returns a schema
    /// containing only the tables that were actually found; null when the
    /// metadata could not be read. Never throws for operational failures
    /// and never fabricates metadata.
    /// </summary>
    /// <param name="tableNames">Table names to read (qualified or not).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DatabaseSchema?> GetTablesSchemaAsync(
        IReadOnlyCollection<string> tableNames,
        CancellationToken cancellationToken = default);
}