using Microsoft.Data.SqlClient;
using SqlOptimizer.Application.Options;

namespace SqlOptimizer.Api.Health;

/// <summary>
/// Fast, read-only reachability probe for the configured SQL Server, used only
/// by <c>GET /api/v1/health</c>. It opens a single connection with a short,
/// capped timeout and runs <c>SELECT 1</c>; it never runs user SQL, never
/// touches user data and never reports success it did not measure. The
/// connection string is used only internally and is never logged, thrown or
/// returned. When no database is configured the probe reports not configured
/// without opening any connection.
/// </summary>
public sealed class DatabaseHealthProbe
{
    /// <summary>Hard ceiling for the ping timeout regardless of configuration.</summary>
    private const int MaxPingTimeoutSeconds = 5;

    private readonly DatabaseOptions _databaseOptions;

    /// <summary>Creates a new probe bound to the configured <see cref="DatabaseOptions"/>.</summary>
    /// <param name="databaseOptions">Database options (connection string, timeouts).</param>
    /// <param name="healthCheckTimeoutSeconds">Configured ping timeout in seconds (capped at 5).</param>
    public DatabaseHealthProbe(DatabaseOptions databaseOptions, int healthCheckTimeoutSeconds)
    {
        _databaseOptions = databaseOptions ?? throw new ArgumentNullException(nameof(databaseOptions));
        TimeoutSeconds = Math.Clamp(healthCheckTimeoutSeconds, 1, MaxPingTimeoutSeconds);
    }

    /// <summary>Effective ping timeout in seconds.</summary>
    public int TimeoutSeconds { get; }

    /// <summary>True when a database connection is configured and the provider is enabled.</summary>
    public bool IsConfigured =>
        _databaseOptions.Enabled && !string.IsNullOrWhiteSpace(_databaseOptions.ConnectionString);

    /// <summary>
    /// Pings the configured database. Returns false when not configured or
    /// when the connection or the <c>SELECT 1</c> probe fails or times out.
    /// Never throws for operational failures and never leaks the connection
    /// string.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> PingAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return false;
        }

        var builder = new SqlConnectionStringBuilder(_databaseOptions.ConnectionString)
        {
            ConnectTimeout = TimeoutSeconds
        };

        try
        {
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT 1", connection)
            {
                CommandTimeout = TimeoutSeconds
            };
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Operational failure (unreachable, auth, timeout): the probe is a
            // liveness signal only, so it degrades to "not reachable". The
            // connection string is never part of any exception or log here.
            return false;
        }
    }
}
