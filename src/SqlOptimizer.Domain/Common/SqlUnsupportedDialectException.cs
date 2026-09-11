namespace SqlOptimizer.Domain.Common;

/// <summary>
/// Thrown when a request targets a dialect that the pipeline does not implement.
/// Maps to the <c>SQL_UNSUPPORTED_DIALECT</c> API error code.
/// </summary>
public sealed class SqlUnsupportedDialectException : SqlOptimizerException
{
    /// <summary>Creates a new instance with the specified dialect.</summary>
    /// <param name="dialect">The unsupported dialect that was requested.</param>
    public SqlUnsupportedDialectException(SqlDialect dialect)
        : base($"SQL dialect '{dialect}' is not supported. Supported dialects: SqlServer.")
    {
        Dialect = dialect;
    }

    /// <summary>The dialect that was requested but is not implemented.</summary>
    public SqlDialect Dialect { get; }
}
