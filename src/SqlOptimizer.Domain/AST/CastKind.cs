namespace SqlOptimizer.Domain.AST;

/// <summary>
/// Variant of a data conversion expression.
/// </summary>
public enum CastKind
{
    /// <summary><c>CAST(expression AS type)</c>.</summary>
    Cast,

    /// <summary><c>CONVERT(type, expression)</c>.</summary>
    Convert,

    /// <summary><c>TRY_CONVERT(type, expression)</c> (returns NULL on failure).</summary>
    TryConvert
}