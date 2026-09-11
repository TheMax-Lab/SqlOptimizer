namespace SqlOptimizer.Domain.Common;

/// <summary>
/// Thrown when the analysis pipeline fails for reasons other than parsing or
/// input validation. Maps to the <c>SQL_ANALYSIS_ERROR</c> API error code.
/// </summary>
public sealed class SqlAnalysisException : SqlOptimizerException
{
    /// <summary>Creates a new instance with the specified message.</summary>
    /// <param name="message">Description of the analysis failure.</param>
    public SqlAnalysisException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new instance with the specified message and inner exception.</summary>
    /// <param name="message">Description of the analysis failure.</param>
    /// <param name="innerException">The underlying failure.</param>
    public SqlAnalysisException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
