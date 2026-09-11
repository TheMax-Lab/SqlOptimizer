namespace SqlOptimizer.Domain.Common;

/// <summary>
/// Thrown when a request input is missing, empty or exceeds configured limits.
/// Maps to the <c>SQL_INVALID_INPUT</c> API error code.
/// </summary>
public sealed class SqlInvalidInputException : SqlOptimizerException
{
    /// <summary>Creates a new instance with the specified message.</summary>
    /// <param name="message">Description of the invalid input.</param>
    public SqlInvalidInputException(string message)
        : base(message)
    {
    }
}
