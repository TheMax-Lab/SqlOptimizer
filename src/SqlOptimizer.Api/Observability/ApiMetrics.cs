namespace SqlOptimizer.Api.Observability;

/// <summary>
/// In-process, lock-free request counters for the public API (requests per
/// endpoint and validation outcomes). Pure observability: it never influences
/// pipeline behavior and holds no request content. A production deployment
/// would forward the structured logs to a metrics backend instead.
/// </summary>
public sealed class ApiMetrics
{
    private long _analyzeRequests;
    private long _optimizeRequests;
    private long _validateRequests;
    private long _passedOutcomes;
    private long _failedOutcomes;
    private long _inconclusiveOutcomes;
    private long _rejectedRequests;

    /// <summary>Records a request for the given endpoint name.</summary>
    /// <param name="endpoint">"analyze", "optimize" or "validate".</param>
    public void Request(string endpoint)
    {
        switch (endpoint)
        {
            case "analyze": Interlocked.Increment(ref _analyzeRequests); break;
            case "optimize": Interlocked.Increment(ref _optimizeRequests); break;
            case "validate": Interlocked.Increment(ref _validateRequests); break;
        }
    }

    /// <summary>Records a rejected request (for example over length limits).</summary>
    public void Rejected() => Interlocked.Increment(ref _rejectedRequests);

    /// <summary>Records a validation outcome (Passed/Failed/Inconclusive; other statuses are not counted).</summary>
    /// <param name="status">The validation status string.</param>
    public void ValidationOutcome(string status)
    {
        switch (status)
        {
            case "Passed": Interlocked.Increment(ref _passedOutcomes); break;
            case "Failed": Interlocked.Increment(ref _failedOutcomes); break;
            case "Inconclusive": Interlocked.Increment(ref _inconclusiveOutcomes); break;
        }
    }

    /// <summary>Snapshot of the current counters (for diagnostics and tests).</summary>
    public (long Analyze, long Optimize, long Validate, long Passed, long Failed, long Inconclusive, long Rejected) Snapshot() =>
        (
            Interlocked.Read(ref _analyzeRequests),
            Interlocked.Read(ref _optimizeRequests),
            Interlocked.Read(ref _validateRequests),
            Interlocked.Read(ref _passedOutcomes),
            Interlocked.Read(ref _failedOutcomes),
            Interlocked.Read(ref _inconclusiveOutcomes),
            Interlocked.Read(ref _rejectedRequests));
}
