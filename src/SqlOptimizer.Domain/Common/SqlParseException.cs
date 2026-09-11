namespace SqlOptimizer.Domain.Common;

/// <summary>
/// Thrown when a SQL statement cannot be parsed into the SqlOptimizer AST.
/// Maps to the <c>SQL_PARSE_ERROR</c> API error code.
/// </summary>
public sealed class SqlParseException : SqlOptimizerException
{
    /// <summary>Creates a new instance with the specified message.</summary>
    /// <param name="message">Description of the parse failure.</param>
    public SqlParseException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new instance with the specified message and inner exception.</summary>
    /// <param name="message">Description of the parse failure.</param>
    /// <param name="innerException">The underlying parser failure.</param>
    public SqlParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
