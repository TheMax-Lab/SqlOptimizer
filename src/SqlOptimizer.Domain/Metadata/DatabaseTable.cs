namespace SqlOptimizer.Domain.Metadata;

/// <summary>
/// Describes a database table with its columns and indexes.
/// </summary>
/// <param name="Schema">Schema name (for example <c>dbo</c>).</param>
/// <param name="Name">Table name.</param>
/// <param name="EstimatedRowCount">Estimated row count when available.</param>
/// <param name="Columns">Table columns.</param>
/// <param name="Indexes">Table indexes.</param>
public sealed record DatabaseTable(
    string Schema,
    string Name,
    long? EstimatedRowCount,
    IReadOnlyList<DatabaseColumn> Columns,
    IReadOnlyList<DatabaseIndex> Indexes)
{
    /// <summary>Looks up a column by (case insensitive) name, or null.</summary>
    /// <param name="columnName">Column name.</param>
    public DatabaseColumn? FindColumn(string columnName) =>
        Columns.FirstOrDefault(c =>
            string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the given column is part of a unique key (primary key or unique index).</summary>
    /// <param name="columnName">Column name.</param>
    public bool IsUniqueKey(string columnName)
    {
        var column = FindColumn(columnName);
        if (column is { PrimaryKey: true })
        {
            return true;
        }

        return Indexes.Any(i => i.Unique && i.KeyColumns.Count == 1 &&
            string.Equals(i.KeyColumns[0], columnName, StringComparison.OrdinalIgnoreCase));
    }
}
