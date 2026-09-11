namespace SqlOptimizer.Domain.Common;

/// <summary>
/// Thrown when a database operation fails. Maps to the <c>DATABASE_ERROR</c>
/// API error code.
/// </summary>
public sealed class SqlDatabaseException : SqlOptimizerException
{
    /// <summary>Creates a new instance with the specified message.</summary>
    /// <param name="message">Description of the database failure.</param>
    public SqlDatabaseException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new instance with the specified message and inner exception.</summary>
    /// <param name="message">Description of the database failure.</param>
    /// <param name="innerException">The underlying database failure.</param>
    public SqlDatabaseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
