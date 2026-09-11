namespace SqlOptimizer.Application.DTOs;

/// <summary>
/// A single semantic-risk flag raised during validation: a dimension in which
/// the candidate may behave differently from the original query, and why.
/// </summary>
/// <param name="Type">The kind of risk.</param>
/// <param name="Description">The concrete explanation for this candidate pair.</param>
/// <param name="Provable">
/// True when the risk is a provable behavioral difference established from
/// structure alone, not merely a suspicion that requires data to confirm.
/// </param>
public sealed record SemanticRisk(SemanticRiskType Type, string Description, bool Provable = false)
{
}
