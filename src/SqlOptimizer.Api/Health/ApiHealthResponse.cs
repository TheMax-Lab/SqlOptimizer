namespace SqlOptimizer.Api.Health;

/// <summary>
/// Response of <c>GET /api/v1/health</c>. <c>Status</c> is <c>Healthy</c> when
/// the application is alive and either no database is configured or the
/// configured database is reachable; it is <c>Degraded</c> when a database is
/// configured but the reachability ping failed. The HTTP status stays 200 in
/// both cases: the application works fully without a database, so a degraded
/// database is a machine-readable signal, not an application failure.
/// </summary>
/// <param name="Status"><c>Healthy</c> or <c>Degraded</c>.</param>
/// <param name="TimestampUtc">UTC time the health state was computed.</param>
/// <param name="Database">Database provider state (no secrets).</param>
public sealed record ApiHealthResponse(
    string Status,
    DateTimeOffset TimestampUtc,
    ApiHealthDatabase Database);

