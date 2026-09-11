using Microsoft.Data.SqlClient;
using SqlOptimizer.Application.Options;
using Testcontainers.MsSql;
using Xunit;

namespace SqlOptimizer.Infrastructure.IntegrationTests;

/// <summary>
/// Shared, disposable SQL Server for all M6 integration tests. Provisions a
/// single Testcontainers container per test collection with a dedicated test
/// database, enables snapshot isolation (required for a consistent
/// original-vs-candidate comparison) and creates a fixed schema with a fixed
/// seed data set (known NULLs, duplicates and row counts). When Docker is
/// unavailable the fixture fails fast with an explicit "integration
/// environment unavailable" message; live database results are never
/// fabricated.
///
/// Alternatively, when the <see cref="ExternalServerEnvironmentVariable"/>
/// environment variable holds a connection string to a dedicated test SQL
/// Server, the fixture uses that server as-is (no container), so the
/// integration tests can run on Docker-less machines. The idempotent
/// schema/seed setup is identical in both modes; the external server is
/// never destroyed on disposal.
/// </summary>
public sealed class TestSqlServerFixture : IAsyncLifetime
{
    /// <summary>Disposable SQL Server 2022 image.</summary>
    public const string Image = "mcr.microsoft.com/mssql/server:2022-latest";

    /// <summary>
    /// Environment variable holding the connection string of an externally
    /// supplied, dedicated test SQL Server. When set and non-empty it takes
    /// precedence over the Testcontainers-provisioned container. The value
    /// must not contain shared credentials; Windows Integrated
    /// Authentication is the supported mode for external servers.
    /// </summary>
    public const string ExternalServerEnvironmentVariable = "SQLOPTIMIZER_INTEGRATION_SQLSERVER";

    private const string Password = "SqlOptimizer!Test1";

    private const string Database = "SqlOptimizerDb";

    private MsSqlContainer? _container;

    /// <summary>Connection string of the disposable SQL Server (never logged by the providers).</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// True when the target database allows snapshot isolation (a hard
    /// prerequisite of the production runtime comparison and guarded
    /// execution). Some SQL Server-compatible engines do not support
    /// <c>ALTER DATABASE ... SET ALLOW_SNAPSHOT_ISOLATION</c> at all; in that
    /// case the prerequisite cannot be met and the dependent tests must be
    /// skipped, not passed or failed.
    /// </summary>
    public bool SnapshotIsolationAvailable { get; private set; }

    /// <summary>
    /// Gate for tests that require the production snapshot-isolation path
    /// (runtime result comparison, guarded execution with evidence). When the
    /// prerequisite cannot be met on the target engine the gate fails fast
    /// with an explicit "integration environment unavailable" message — the
    /// same convention the fixture already uses when Docker is unavailable —
    /// so the test is reported as NOT EXECUTED (environmental limitation),
    /// never as passed, and no assertion is ever weakened.
    /// </summary>
    public void RequireSnapshotIsolation()
    {
        if (SnapshotIsolationAvailable)
        {
            return;
        }

        throw new InvalidOperationException(
            "Integration environment unavailable: snapshot isolation is not enabled on the target " +
            "SQL Server and cannot be enabled (ALTER DATABASE ... SET ALLOW_SNAPSHOT_ISOLATION is not " +
            "supported by this engine). The runtime comparison/execution path requires it by design; " +
            "this test was not executed and no live database verification is claimed for it.");
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var externalConnectionString = Environment.GetEnvironmentVariable(ExternalServerEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(externalConnectionString))
        {
            ConnectionString = externalConnectionString.Trim();
        }
        else
        {
            MsSqlContainer? container = null;
            try
            {
                container = new MsSqlBuilder(Image)
                    .WithPassword(Password)
                    .WithDatabase(Database)
                    .Build();

                await container.StartAsync();
            }
            catch (OperationCanceledException)
            {
                await SafeDisposeAsync(container);
                throw;
            }
            catch (Exception ex)
            {
                await SafeDisposeAsync(container);
                throw new InvalidOperationException(
                    "Integration environment unavailable: Docker/Testcontainers could not provision the disposable SQL Server " +
                    $"({ex.Message.Trim()}). These integration tests require a Docker-capable machine " +
                    $"or the {ExternalServerEnvironmentVariable} environment variable pointing at a dedicated test " +
                    "SQL Server; they were not executed and no live database verification is claimed.",
                    ex);
            }

            _container = container;
            ConnectionString = container.GetConnectionString();
        }

        await EnableSnapshotIsolationAsync(CancellationToken.None);
        await CreateTestSchemaAsync(CancellationToken.None);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await SafeDisposeAsync(_container);
        _container = null;
    }

    /// <summary>
    /// Builds <see cref="DatabaseOptions"/> pointing at the disposable server
    /// with the given limits.
    /// </summary>
    /// <param name="commandTimeoutSeconds">Command timeout in seconds.</param>
    /// <param name="connectionTimeoutSeconds">Connection timeout in seconds.</param>
    /// <param name="maxRowsForComparison">Server-wide comparison row cap.</param>
    /// <param name="maxResultCells">Server-wide result cell cap.</param>
    public DatabaseOptions CreateOptions(
        int commandTimeoutSeconds = 30,
        int connectionTimeoutSeconds = 15,
        int maxRowsForComparison = 1_000,
        int maxResultCells = 20_000) => new(
        ConnectionString: ConnectionString,
        Enabled: true,
        CommandTimeoutSeconds: commandTimeoutSeconds,
        ConnectionTimeoutSeconds: connectionTimeoutSeconds,
        MaxRowsForComparison: maxRowsForComparison,
        MaxResultCells: maxResultCells,
        ApplicationName: "SqlOptimizerIntegrationTests");

    /// <summary>Runs a scalar query against the disposable server (test assertions only).</summary>
    /// <param name="sql">The read-only query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<object?> QueryScalarAsync(string sql, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        return await command.ExecuteScalarAsync(cancellationToken);
    }
    private static async Task SafeDisposeAsync(MsSqlContainer? container)
    {
        if (container is null)
        {
            return;
        }

        try
        {
            await container.DisposeAsync();
        }
        catch
        {
            // Best effort: the test outcome is already decided.
        }
    }

    private async Task EnableSnapshotIsolationAsync(CancellationToken cancellationToken)
    {
        // The target database is the one actually in use (the disposable
        // container database or the external server's database).
        var database = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog ?? Database;

        var builder = new SqlConnectionStringBuilder(ConnectionString)
        {
            InitialCatalog = "master",
            ConnectTimeout = 15
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        if (await SnapshotIsolationAvailableAsync(connection, cancellationToken))
        {
            SnapshotIsolationAvailable = true;
            return;
        }

        // Some engines reject "WITH NO WAIT" (unsupported option) while
        // accepting the plain "ON" form, and vice versa; try both so the
        // prerequisite can be met on fresh environments.
        foreach (var statement in new[]
        {
            $"ALTER DATABASE [{database}] SET ALLOW_SNAPSHOT_ISOLATION WITH NO WAIT;",
            $"ALTER DATABASE [{database}] SET ALLOW_SNAPSHOT_ISOLATION ON;"
        })
        {
            try
            {
                await using var command = new SqlCommand(statement, connection)
                {
                    CommandTimeout = 60
                };

                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqlException)
            {
                // This syntax was refused by the engine; try the next one.
            }
        }

        // If the engine did not support enabling snapshot isolation (or it
        // refused every change): the prerequisite cannot be met. Dependent
        // tests are skipped via RequireSnapshotIsolation(); the product
        // contract is not weakened.
        SnapshotIsolationAvailable = await SnapshotIsolationAvailableAsync(connection, cancellationToken);
    }

    /// <summary>
    /// Functionally probes whether the target database allows snapshot
    /// isolation, using the exact statement sequence the production
    /// providers use (SET XACT_ABORT, SET ... SNAPSHOT, BEGIN, a data read,
    /// ROLLBACK). SQL raises error 3960/3952 when ALLOW_SNAPSHOT_ISOLATION
    /// is not enabled; the data read is required because some engines only
    /// enforce the setting when a snapshot transaction actually accesses
    /// data. A functional probe is used instead of a catalog lookup
    /// (DBProperty/sys.database_properties) because some SQL Server engines
    /// do not expose those catalog surfaces.
    /// </summary>
    private static async Task<bool> SnapshotIsolationAvailableAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var probe = new SqlCommand(
                "SET XACT_ABORT ON;\nSET TRANSACTION ISOLATION LEVEL SNAPSHOT;\nBEGIN TRANSACTION;\n" +
                "SELECT COUNT(*) FROM sys.tables;\nROLLBACK;",
                connection)
            {
                CommandTimeout = 30
            };
            await probe.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (SqlException ex) when (ex.Number == 3952 || ex.Number == 3960)
        {
            return false;
        }
    }

    private Task CreateTestSchemaAsync(CancellationToken cancellationToken) => ExecuteSetupAsync(
        """
        IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
            CREATE TABLE dbo.Customers (Id INT NOT NULL, Name NVARCHAR(100) NOT NULL, City NVARCHAR(50) NULL, CONSTRAINT PK_Customers PRIMARY KEY (Id));
        IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL
            CREATE TABLE dbo.Orders (OrderId INT NOT NULL, CustomerId INT NOT NULL, Total MONEY NULL, CONSTRAINT PK_Orders PRIMARY KEY (OrderId));
        IF OBJECT_ID(N'dbo.Bench', N'U') IS NULL
            CREATE TABLE dbo.Bench (N INT NOT NULL, CONSTRAINT PK_Bench PRIMARY KEY (N));
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_City')
            CREATE INDEX IX_Customers_City ON dbo.Customers (City);

        IF NOT EXISTS (SELECT 1 FROM dbo.Customers)
        BEGIN
            INSERT INTO dbo.Customers (Id, Name, City) VALUES
                (1, N'Ada Lovelace', N'Rome'),
                (2, N'Alan Turing', N'London'),
                (3, N'Grace Hopper', NULL),
                (4, N'Edsger Dijkstra', N'Rome');

            INSERT INTO dbo.Orders (OrderId, CustomerId, Total) VALUES
                (100, 1, 10.50),
                (101, 1, NULL),
                (102, 2, 20.00),
                (103, 3, 5.75);

            ;WITH Numbers AS
            (
                SELECT TOP (100) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS N
                FROM sys.all_columns a
                CROSS JOIN sys.all_columns b
            )
            INSERT INTO dbo.Bench (N)
            SELECT N FROM Numbers;
        END
        """, cancellationToken);

    private async Task ExecuteSetupAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 120
        };

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
