namespace SqlOptimizer.Application.DTOs;

/// <summary>
/// Lifecycle status of an optimization candidate. A candidate is created as
/// <see cref="Generated"/>; only the outcome of <c>ISqlValidator</c> can move
/// it to <see cref="Validated"/>, <see cref="Rejected"/> or
/// <see cref="Inconclusive"/>. Generation code must never set
/// <see cref="Validated"/> directly.
/// </summary>
public enum CandidateStatus
{
    /// <summary>The candidate was generated; validation has not run yet.</summary>
    Generated,

    /// <summary>Validation is in progress (transient state only).</summary>
    ValidationPending,

    /// <summary>Validation passed; the candidate preserves the result contract.</summary>
    Validated,

    /// <summary>Validation proved a semantic difference; the candidate must not be used.</summary>
    Rejected,

    /// <summary>
    /// Validation could not establish equivalence. The candidate is preserved
    /// but is never presented as safe or as an applied optimization.
    /// </summary>
    Inconclusive
}
