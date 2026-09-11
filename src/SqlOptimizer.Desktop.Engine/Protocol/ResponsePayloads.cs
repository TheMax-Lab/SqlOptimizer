using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Common;

namespace SqlOptimizer.Desktop.Engine.Protocol;

/// <summary>
/// Response payload records of the desktop engine protocol. They are thin
/// projections of the existing Application/domain results (the same
/// projection the HTTP API performs); no business logic lives here.
/// </summary>

/// <summary>Result of the <c>configure</c> operation.</summary>
/// <param name="Configured">True when the pipeline was (re)configured successfully.</param>
public sealed record ConfiguredPayload(bool Configured);

/// <summary>Result of the <c>ping</c> operation.</summary>
/// <param name="Pong">Always true on a successful response.</param>
public sealed record PingPayload(bool Pong);

/// <summary>Result of the <c>cancel</c> operation.</summary>
/// <param name="Cancelled">True when an in-flight request with that id was found and canceled.</param>
public sealed record CancelResultPayload(bool Cancelled);

/// <summary>Result of the <c>shutdown</c> operation.</summary>
/// <param name="Accepted">True when the engine accepted the shutdown request.</param>
public sealed record ShutdownResultPayload(bool Accepted);

/// <summary>
/// Result of the <c>health</c> operation (local equivalent of the HTTP API's
/// <c>GET /api/v1/health</c> plus LLM readiness).
/// </summary>
/// <param name="Status">"Healthy" when no database is configured or the configured database is reachable; "Degraded" otherwise.</param>
/// <param name="TimestampUtc">Probe timestamp.</param>
/// <param name="Database">Database readiness block.</param>
/// <param name="Llm">LLM readiness block.</param>
public sealed record HealthResponsePayload(
    string Status,
    DateTimeOffset TimestampUtc,
    HealthDatabasePayload Database,
    HealthLlmPayload Llm);

/// <summary>Database readiness block of the health result.</summary>
/// <param name="Configured">True when a connection string is configured and the provider is enabled.</param>
/// <param name="Reachable">True when a <c>SELECT 1</c> probe succeeded.</param>
/// <param name="MetadataCacheTtlSeconds">Configured metadata cache TTL (null when not configured).</param>
public sealed record HealthDatabasePayload(bool Configured, bool Reachable, int? MetadataCacheTtlSeconds);

/// <summary>LLM readiness block of the health result.</summary>
/// <param name="Provider">Configured provider name (empty when disabled).</param>
/// <param name="Configured">True when a real LLM client (OpenAI-compatible or Mock) is active.</param>
/// <param name="Model">Configured model identifier.</param>
public sealed record HealthLlmPayload(string Provider, bool Configured, string Model);

/// <summary>
/// Result of the <c>analyze</c> operation. Mirrors the HTTP API's
/// <c>AnalysisResponse</c>: the AST is only included when the request asked
/// for it.
/// </summary>
/// <param name="Sql">The analyzed SQL text.</param>
/// <param name="Dialect">The dialect of the analyzed query.</param>
/// <param name="ComplexityScore">Deterministic complexity score (0-100).</param>
/// <param name="PerformanceScore">Deterministic performance risk score (0-100).</param>
/// <param name="Findings">Findings produced by the rule engine.</param>
/// <param name="Statistics">Structural statistics of the query.</param>
/// <param name="Ast">The parsed AST, only when the request asked for it.</param>
public sealed record AnalyzeResponsePayload(
    string Sql,
    SqlDialect Dialect,
    int ComplexityScore,
    int PerformanceScore,
    IReadOnlyList<SqlFinding> Findings,
    QueryStatistics Statistics,
    SelectStatement? Ast);
