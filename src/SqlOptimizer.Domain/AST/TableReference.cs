using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// A reference to a base table in a FROM clause.
/// </summary>
/// <param name="Schema">Schema name when qualified (for example <c>dbo</c>), otherwise empty.</param>
/// <param name="Name">Table name.</param>
/// <param name="Alias">Table alias when present.</param>
public sealed record TableReference(string Schema, string Name, string? Alias) : FromSource
{
    /// <summary>
    /// The name used to qualify columns: the alias when present, otherwise
    /// the table name.
    /// </summary>
    public string Qualifier => Alias ?? Name;

    /// <summary>Full name including schema when qualified.</summary>
    public string FullName => string.IsNullOrEmpty(Schema) ? Name : $"{Schema}.{Name}";

    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children => [];
}
