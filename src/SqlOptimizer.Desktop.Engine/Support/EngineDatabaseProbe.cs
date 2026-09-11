using Microsoft.Data.SqlClient;
using SqlOptimizer.Application.Options;

namespace SqlOptimizer.Desktop.Engine.Support;

/// <summary>
/// Fast, read-only reachability probe for the configured SQL Server, used by
/// the <c>health</c> operation. It mirrors the semantics of the HTTP API's
/// <c>DatabaseHealthProbe</c> (that type lives in the ASP.NET project which
/// this host must not reference): a single <c>SELECT 1</c> over one
/// connection with a short capped timeout. The probe never runs user SQL,
/// never touches user data and never reports success it did not measure.
/// The connection string is used only internally and is never logged, thrown
/// or returned. When no database is configured it reports not configured
/// without opening any connection.
/// </summary>
public sealed class EngineDatabaseProbe
{
    /// <summary>Hard ceiling for the ping timeout regardless of configuration.</summary>
    private const int MaxPingTimeoutSeconds = 5;

    private readonly DatabaseOptions _databaseOptions;

    /// <summary>Creates a new probe bound to the configured <paramref name="databaseOptions"/>.</summary>
    /// <param name="databaseOptions">The database options.</param>
    public EngineDatabaseProbe(DatabaseOptions databaseOptions)
    {
        _databaseOptions = databaseOptions ?? throw new ArgumentNullException(nameof(databaseOptions));
    }

    /// <summary>Effective ping timeout in seconds (capped at 5).</summary>
    public int TimeoutSeconds => Math.Clamp(_databaseOptions.ConnectionTimeoutSeconds, 1, MaxPingTimeoutSeconds);

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
