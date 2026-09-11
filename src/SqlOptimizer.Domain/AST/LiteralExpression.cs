using System.Text.Json.Serialization;

namespace SqlOptimizer.Domain.AST;

/// <summary>
/// A literal value. The value is preserved as source text to keep analysis
/// deterministic and lossless; <see cref="DataType"/> carries the apparent
/// type family of the literal (for example <c>int</c>, <c>nvarchar</c>,
/// <c>datetime</c>, <c>bit</c>, <c>null</c>).
/// </summary>
/// <param name="Value">Literal text exactly as written in the query.</param>
/// <param name="DataType">Apparent data type family of the literal.</param>
public sealed record LiteralExpression(string Value, string DataType) : SqlExpression
{
    /// <summary>True when the literal is the NULL keyword.</summary>
    public bool IsNull => string.Equals(DataType, "null", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the literal is a character based value.</summary>
    public bool IsString => DataType is "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext";

    /// <summary>True when the literal is numeric (int, decimal, float, bit, money...).</summary>
    public bool IsNumeric => DataType is "int" or "bigint" or "smallint" or "tinyint" or "decimal"
        or "numeric" or "float" or "real" or "bit" or "money" or "smallmoney";

    /// <summary>True when the literal is a date/time value.</summary>
    public bool IsDateTime => DataType is "date" or "time" or "datetime" or "datetime2" or "smalldatetime" or "offset";

    /// <inheritdoc />
    [JsonIgnore]
    public override IEnumerable<SqlNode> Children => [];
}
