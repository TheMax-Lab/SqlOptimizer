namespace SqlOptimizer.Domain.Common;

/// <summary>
/// Thrown when an LLM response cannot be parsed into a valid optimization
/// response. The current <c>LlmResponseParser</c> degrades invalid responses
/// to <c>null</c> instead of throwing, and <c>LlmCandidateGenerator</c>
/// reports an explicit limitation, so the pipeline never crashes on bad
/// provider output.
/// </summary>
public sealed class LlmInvalidResponseException : LlmException
{
    /// <summary>Creates a new instance with the specified message.</summary>
    /// <param name="message">Description of the invalid response.</param>
    public LlmInvalidResponseException(string message)
        : base(message)
    {
    }
}
