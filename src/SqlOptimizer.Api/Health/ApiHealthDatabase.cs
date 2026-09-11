namespace SqlOptimizer.Api.Health;

/// <summary>
/// Machine-readable state of the optional database-backed validation and
/// metadata providers, as reported by <c>GET /api/v1/health</c>. Contains no
/// connection string, credential or other secret.
/// </summary>
/// <param name="Configured">True when a database connection is configured and the provider is enabled.</param>
/// <param name="Reachable">
/// True when the configured database answered the reachability ping; false when
/// not configured or when the ping failed or timed out.
/// </param>
/// <param name="MetadataCacheTtlSeconds">Metadata cache TTL when a database is configured (non-secret configuration detail).</param>
public sealed record ApiHealthDatabase(
    bool Configured,
    bool Reachable,
    int? MetadataCacheTtlSeconds);
