namespace SqlOptimizer.Domain.Common;

/// <summary>
/// Thrown when an LLM is requested but no valid LLM configuration exists.
/// <c>LlmCandidateGenerator</c> converts it into an explicit limitation so
/// the deterministic pipeline continues without LLM candidates.
/// </summary>
public sealed class LlmNotConfiguredException : LlmException
{
    /// <summary>Creates a new instance with the specified message.</summary>
    /// <param name="message">Description of the missing configuration.</param>
    public LlmNotConfiguredException(string message)
        : base(message)
    {
    }
}
