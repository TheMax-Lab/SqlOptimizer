namespace SqlOptimizer.Domain.Metadata;

/// <summary>
/// Describes a database column.
/// </summary>
/// <param name="Name">Column name.</param>
/// <param name="DataType">Data type as reported by the database (for example <c>INT</c>, <c>NVARCHAR(100)</c>).</param>
/// <param name="Nullable">True when the column allows NULL.</param>
/// <param name="PrimaryKey">True when the column is part of the primary key.</param>
public sealed record DatabaseColumn(
    string Name,
    string DataType,
    bool Nullable,
    bool PrimaryKey);
