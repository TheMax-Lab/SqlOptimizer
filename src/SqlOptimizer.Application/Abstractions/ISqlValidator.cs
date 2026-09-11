using SqlOptimizer.Application.DTOs;

namespace SqlOptimizer.Application.Abstractions;

/// <summary>
/// Validates an optimized SQL candidate against the original query.
/// </summary>
public interface ISqlValidator
{
    /// <summary>
    /// Validates a candidate. Never claims semantic equivalence without
    /// actual evidence; returns <c>Inconclusive</c> when validation cannot
    /// be performed.
    /// </summary>
    /// <param name="request">Validation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ValidationResult> ValidateAsync(
        SqlValidationRequest request,
        CancellationToken cancellationToken = default);
}
