namespace SqlOptimizer.Domain.Common;

/// <summary>
/// Thrown when SQL validation fails because of a validation pipeline error
/// (as opposed to a candidate that simply did not pass validation). Maps to
/// the <c>VALIDATION_ERROR</c> API error code.
/// </summary>
public sealed class SqlValidationException : SqlOptimizerException
{
    /// <summary>Creates a new instance with the specified message.</summary>
    /// <param name="message">Description of the validation failure.</param>
    public SqlValidationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new instance with the specified message and inner exception.</summary>
    /// <param name="message">Description of the validation failure.</param>
    /// <param name="innerException">The underlying failure.</param>
    public SqlValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
