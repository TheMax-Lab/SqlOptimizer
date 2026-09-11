namespace SqlOptimizer.Domain.Common;

/// <summary>
/// Base exception for LLM related failures. The LLM is an optional
/// capability: <c>LlmCandidateGenerator</c> converts these failures into
/// explicit candidate-generation limitations so the deterministic pipeline
/// continues. If an LLM failure ever reaches the API boundary it is covered
/// by the generic sanitized 500 mapping (no LLM details are echoed).
/// </summary>
public class LlmException : SqlOptimizerException
{
    /// <summary>Creates a new instance with the specified message.</summary>
    /// <param name="message">Description of the LLM failure.</param>
    public LlmException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new instance with the specified message and inner exception.</summary>
    /// <param name="message">Description of the LLM failure.</param>
    /// <param name="innerException">The underlying failure.</param>
    public LlmException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
