using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;

namespace SqlOptimizer.Application.DTOs;

/// <summary>
/// Request to validate an optimized SQL candidate against the original query.
/// </summary>
/// <param name="OriginalSql">The original SQL text.</param>
/// <param name="CandidateSql">The candidate SQL text (untrusted).</param>
/// <param name="CompareResults">Compare result sets when runtime execution is allowed.</param>
/// <param name="MaxRowsForComparison">Safety cap on rows compared.</param>
/// <param name="Dialect">The SQL dialect (default: SqlServer).</param>
/// <param name="Schema">Optional schema metadata; enables metadata-backed checks such as star-expansion proof and COUNT(*) equivalence.</param>
public sealed record SqlValidationRequest(
    string OriginalSql,
    string CandidateSql,
    bool CompareResults,
    int MaxRowsForComparison = 1000,
    SqlDialect Dialect = SqlDialect.SqlServer,
    DatabaseSchema? Schema = null);

