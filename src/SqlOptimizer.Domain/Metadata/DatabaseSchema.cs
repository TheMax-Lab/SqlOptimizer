using SqlOptimizer.Domain.AST;

namespace SqlOptimizer.Domain.Metadata;

/// <summary>
/// Describes the schema metadata available for analysis: a set of tables with
/// their columns and indexes.
/// </summary>
/// <param name="Tables">Known tables.</param>
public sealed record DatabaseSchema(IReadOnlyList<DatabaseTable> Tables)
{
    /// <summary>Empty schema instance.</summary>
    public static DatabaseSchema Empty { get; } = new(Array.Empty<DatabaseTable>());

    /// <summary>Looks up a table by (case insensitive) name, or null.</summary>
    /// <param name="name">Table name.</param>
    public DatabaseTable? FindTable(string name) =>
        Tables.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Looks up a table by name, optionally qualified with a schema
    /// (for example <c>dbo.Orders</c>), or null.
    /// </summary>
    /// <param name="reference">The table reference to resolve.</param>
    public DatabaseTable? FindTable(TableReference reference)
    {
        if (string.IsNullOrEmpty(reference.Schema))
        {
            return FindTable(reference.Name);
        }

        return Tables.FirstOrDefault(t =>
            string.Equals(t.Name, reference.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.Schema, reference.Schema, StringComparison.OrdinalIgnoreCase));
    }
}
