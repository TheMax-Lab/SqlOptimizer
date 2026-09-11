namespace SqlOptimizer.Application.DTOs;

/// <summary>Status of a validation run.</summary>
public enum ValidationStatus
{
    /// <summary>Validation was not requested.</summary>
    NotRequested,

    /// <summary>
    /// Validation was requested but the executable (runtime) part could not
    /// be run at all (for example runtime validation disabled, no database
    /// provider available, or a dialect mismatch). No query was executed and
    /// no semantic equivalence is claimed.
    /// </summary>
    NotExecuted,

    /// <summary>Validation executed and the candidate passed all checks.</summary>
    Passed,

    /// <summary>Validation executed and the candidate failed at least one check.</summary>
    Failed,

    /// <summary>Validation could not reach a conclusion (for example only syntax could be checked).</summary>
    Inconclusive
}
