namespace SqlOptimizer.Application.Options;

/// <summary>
/// Database connection and validation-execution configuration, bound from
/// the "Database" configuration section (or environment variables). The
/// connection string is supplied only via configuration/environment, is
/// never logged, printed or embedded in exceptions, and with
/// <see cref="Enabled"/> false (the default) no database provider is
/// registered: the application starts and runs fully without a database.
/// </summary>
/// <param name="ConnectionString">SQL Server connection string (secret; supply via environment or user secrets, never commit it).</param>
/// <param name="Enabled">Master switch for the database-backed validation and metadata providers.</param>
/// <param name="CommandTimeoutSeconds">Command timeout applied to every validation statement (default 30).</param>
/// <param name="ConnectionTimeoutSeconds">Connection open timeout (default 15).</param>
/// <param name="MaxRowsForComparison">Server-wide hard cap on the rows compared per query; the effective cap is the minimum of this value and the per-request cap (default 1000).</param>
/// <param name="MaxResultCells">Safety cap on the total number of cells materialized from a single result set (default 20000).</param>
/// <param name="ApplicationName">Application name reported to SQL Server (diagnostics only; default "SqlOptimizer").</param>
/// <param name="MetadataCacheTtlSeconds">Time-to-live of cached live schema metadata (default 300).</param>
/// <param name="MetadataCacheMaxEntries">Bounded cache size for live schema metadata (default 256).</param>
public sealed record DatabaseOptions(
    string ConnectionString = "",
    bool Enabled = false,
    int CommandTimeoutSeconds = 30,
    int ConnectionTimeoutSeconds = 15,
    int MaxRowsForComparison = 1_000,
    int MaxResultCells = 20_000,
    string ApplicationName = "SqlOptimizer",
    int MetadataCacheTtlSeconds = 300,
    int MetadataCacheMaxEntries = 256);
