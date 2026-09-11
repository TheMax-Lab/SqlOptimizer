namespace SqlOptimizer.Domain.Common;

/// <summary>
/// Base exception for all SqlOptimizer failures. Carries no technical details
/// that should never be exposed; the API layer maps concrete subclasses to
/// structured error codes.
/// </summary>
public class SqlOptimizerException : Exception
{
    /// <summary>Creates a new instance with the specified message.</summary>
    /// <param name="message">Human readable description of the failure.</param>
    public SqlOptimizerException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new instance with the specified message and inner exception.</summary>
    /// <param name="message">Human readable description of the failure.</param>
    /// <param name="innerException">The exception that caused this failure.</param>
    public SqlOptimizerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
