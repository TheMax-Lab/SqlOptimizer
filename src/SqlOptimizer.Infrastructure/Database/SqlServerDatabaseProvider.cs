using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Domain.Parsing;
using SqlOptimizer.Infrastructure.Validation;

namespace SqlOptimizer.Infrastructure.Database;

/// <summary>
/// SQL Server implementation of <see cref="ISqlDatabaseProvider"/>: live
/// schema metadata, estimated execution plans and controlled read-only
/// execution over <see cref="Microsoft.Data.SqlClient"/>. Safety is enforced
/// by code, never by convention:
/// <list type="bullet">
/// <item><see cref="GetSchemaAsync"/> reads the system catalog through the
/// bounded, cached metadata provider; when no database is configured it
/// returns an empty schema (metadata is never fabricated), and a failed
/// read is a controlled <see cref="SqlDatabaseException"/>.</item>
/// <item><see cref="GetExecutionPlanAsync"/> retrieves the estimated plan
/// with <c>SET SHOWPLAN_XML ON</c>, which returns the plan without executing
/// the statement, and maps the XML through the existing
/// <see cref="ExecutionPlanParser"/>. Database failures become a controlled
/// <see cref="SqlDatabaseException"/>; an unparseable plan document surfaces
/// as the controlled <see cref="SqlParseException"/>.</item>
/// <item><see cref="ExecuteAsync"/> only ever executes statements the
/// fail-closed <see cref="TSqlReadOnlyGuard"/> proves to be a single
/// read-only SELECT (DML/DDL/EXEC/transactions and unclassifiable input are
/// rejected with <see cref="SqlSafetyException"/> before anything runs).
/// The statement runs inside a snapshot-isolation transaction that is always
/// rolled back, bounded by the configured row/cell caps, so no state change
/// (even from a side-effecting scalar function) is ever committed.</item>
/// </list>
/// The provider never logs SQL text, parameter values, connection strings or
/// credentials; SQL text is logged only when
/// <see cref="SqlOptimizerOptions.LogSql"/> is explicitly enabled.
/// </summary>
public sealed class SqlServerDatabaseProvider : ISqlDatabaseProvider
{
    /// <summary>SQL Server error number raised when snapshot isolation is not enabled on the database.</summary>
    private const int SqlErrorSnapshotIsolationNotEnabled = 3960;

    private readonly ISqlParser _parser;
    private readonly SqlDatabaseMetadataProvider _metadataProvider;
    private readonly DatabaseOptions _options;
    private readonly SqlOptimizerOptions _pipelineOptions;
    private readonly ILogger<SqlServerDatabaseProvider> _logger;

    /// <summary>
    /// Creates a new provider.
    /// </summary>
    /// <param name="parser">The SQL Server parser (used by the read-only safety guard).</param>
    /// <param name="metadataProvider">The SQL Server metadata provider (shared catalog queries and cache).</param>
    /// <param name="options">Database options (connection and limits).</param>
    /// <param name="pipelineOptions">Global pipeline options (controls SQL logging).</param>
    /// <param name="logger">Logger (never receives SQL text, parameters or connection details).</param>
    public SqlServerDatabaseProvider(
        ISqlParser parser,
        SqlDatabaseMetadataProvider metadataProvider,
        DatabaseOptions options,
        SqlOptimizerOptions pipelineOptions,
        ILogger<SqlServerDatabaseProvider> logger)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _metadataProvider = metadataProvider ?? throw new ArgumentNullException(nameof(metadataProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _pipelineOptions = pipelineOptions ?? throw new ArgumentNullException(nameof(pipelineOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public SqlDialect Dialect => SqlDialect.SqlServer;

    /// <summary>True when a database connection is configured and usable.</summary>
    public bool IsConfigured => _options.Enabled && !string.IsNullOrWhiteSpace(_options.ConnectionString);

    /// <inheritdoc />
    public async Task<DatabaseSchema> GetSchemaAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Database schema retrieval started.");

        if (!IsConfigured)
        {
            _logger.LogInformation("Database schema requested but no database is configured; returning an empty schema.");
            return DatabaseSchema.Empty;
        }

        var schema = await _metadataProvider.GetFullSchemaAsync(cancellationToken).ConfigureAwait(false);
        if (schema is null)
        {
            _logger.LogWarning("Database schema retrieval produced no metadata (the database read failed); reporting a database error.");
            throw new SqlDatabaseException("The database schema could not be read.");
        }

        _logger.LogInformation("Database schema retrieval completed ({Count} tables).", schema.Tables.Count);
        return schema;
    }

    /// <inheritdoc />
    public async Task<ExecutionPlan> GetExecutionPlanAsync(string sql, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new SqlInvalidInputException("The SQL text must not be empty.");
        }

        if (!IsConfigured)
        {
            _logger.LogInformation("Execution plan requested but no database is configured; no plan produced.");
            throw new SqlDatabaseException("The execution plan could not be retrieved: no database is configured.");
        }

        if (_pipelineOptions.LogSql)
        {
            _logger.LogDebug("Retrieving the estimated execution plan for: {Sql}", sql);
        }

        _logger.LogInformation("Execution plan retrieval started.");

        using var connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Execution plan retrieval failed: the database connection could not be opened.");
            throw new SqlDatabaseException("The execution plan could not be retrieved: the database connection failed.", ex);
        }

        try
        {
            var planXml = await ReadShowPlanXmlAsync(connection, sql, cancellationToken).ConfigureAwait(false);
            var plan = ExecutionPlanParser.Parse(planXml);

            _logger.LogInformation("Execution plan retrieval completed ({Count} operators).", plan.Operators.Count);
            return plan;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        // Controlled errors propagate as-is (they carry no connection details):
        // SqlParseException (unparseable plan document), SqlDatabaseException
        // (provider-specific database failure).
        catch (Exception ex) when (ex is not SqlParseException and not SqlDatabaseException)
        {
            _logger.LogWarning(ex, "Execution plan retrieval failed.");
            throw new SqlDatabaseException("The execution plan could not be retrieved.", ex);
        }
    }

    /// <inheritdoc />
    public async Task<QueryExecutionResult> ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        // Fail-closed safety gate: the statement must be provably a single
        // read-only SELECT before anything else happens. Untrusted input that
        // cannot be classified is rejected, never executed.
        var guard = TSqlReadOnlyGuard.Check(sql, _parser);
        if (!guard.IsReadOnly)
        {
            _logger.LogWarning("Read-only execution refused: {Reason}", guard.Reason);
            throw new SqlSafetyException($"The statement was rejected by the read-only safety guard: {guard.Reason}");
        }

        if (!IsConfigured)
        {
            _logger.LogInformation("Read-only execution requested but no database is configured; no execution performed.");
            throw new SqlDatabaseException("The read-only query could not be executed: no database is configured.");
        }

        if (_pipelineOptions.LogSql)
        {
            _logger.LogDebug("Executing the read-only query: {Sql}", sql);
        }

        _logger.LogInformation("Read-only query execution started.");
        var stopwatch = Stopwatch.StartNew();

        using var connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await BeginSnapshotTransactionAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SqlException ex) when (ex.Number == SqlErrorSnapshotIsolationNotEnabled)
        {
            _logger.LogWarning(
                "Read-only query execution refused: snapshot isolation is not enabled on the target database (SQL error {ErrorCode}).",
                ex.Number);
            throw new SqlDatabaseException(
                "The read-only query could not be executed: snapshot isolation is not enabled on the target database. Enable ALLOW_SNAPSHOT_ISOLATION.",
                ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Read-only query execution failed: the database connection could not be opened.");
            throw new SqlDatabaseException("The read-only query could not be executed: the database connection failed.", ex);
        }

        try
        {
            var (columnNames, rowsReturned) = await ReadBoundedResultSetAsync(connection, sql, cancellationToken).ConfigureAwait(false);
            await RollbackBestEffortAsync(connection).ConfigureAwait(false);
            stopwatch.Stop();

            _logger.LogInformation(
                "Read-only query execution completed ({Count} rows in {DurationMs} ms).",
                rowsReturned,
                stopwatch.ElapsedMilliseconds);
            return new QueryExecutionResult(rowsReturned, stopwatch.Elapsed, columnNames);
        }
        catch (OperationCanceledException)
        {
            await RollbackBestEffortAsync(connection).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Read-only query execution failed.");
            await RollbackBestEffortAsync(connection).ConfigureAwait(false);
            throw new SqlDatabaseException("The read-only query could not be executed.", ex);
        }
    }

    /// <summary>
    /// Retrieves the estimated plan XML for the statement. <c>SET SHOWPLAN_XML
    /// ON</c> makes SQL Server return the plan as a result row without
    /// executing the statement, so planning never mutates anything. The
    /// setting is session-scoped, therefore each phase runs in its own batch
    /// (separate command) and the setting is always switched off again before
    /// the connection can be returned to the pool.
    /// </summary>
    private async Task<string> ReadShowPlanXmlAsync(
        SqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using (var enable = new SqlCommand("SET SHOWPLAN_XML ON;", connection)
        {
            CommandTimeout = 30
        })
        {
            await enable.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var command = new SqlCommand(sql, connection)
            {
                CommandTimeout = Math.Max(1, _options.CommandTimeoutSeconds)
            };

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new SqlDatabaseException("The database returned no execution plan.");
            }

            if (reader.IsDBNull(0))
            {
                throw new SqlDatabaseException("The execution plan was empty.");
            }

            return reader.GetString(0);
        }
        finally
        {
            // Best effort: the session setting must not leak to the next user
            // of this (possibly pooled) connection.
            try
            {
                await using var disable = new SqlCommand("SET SHOWPLAN_XML OFF;", connection)
                {
                    CommandTimeout = 5
                };
                await disable.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The connection is closed/disposed by the caller regardless.
            }
        }
    }

    /// <summary>
    /// Runs the (already guard-validated) read-only statement with server-side
    /// <c>SET ROWCOUNT</c> and streaming access. Row values are never
    /// materialized: only the column names and a row count bounded by the
    /// configured caps are returned.
    /// </summary>
    private async Task<(IReadOnlyList<string> ColumnNames, int RowsReturned)> ReadBoundedResultSetAsync(
        SqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        var cap = Math.Max(1, _options.MaxRowsForComparison);
        var fetchLimit = cap + 1;

        await using var command = new SqlCommand($"SET ROWCOUNT {fetchLimit};\n{sql}", connection)
        {
            CommandTimeout = Math.Max(1, _options.CommandTimeoutSeconds)
        };

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var columns = new List<string>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }

        var rows = 0;
        var cells = 0;
        var cellCap = Math.Max(1, _options.MaxResultCells);
        while (rows < fetchLimit)
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                break;
            }

            rows++;
            cells += reader.FieldCount;
            if (cells >= cellCap)
            {
                break;
            }
        }

        return (columns, Math.Min(rows, cap));
    }

    /// <summary>
    /// Configures the session for a non-committed read: XACT_ABORT so errors
    /// never leave a half-open transaction, SNAPSHOT isolation for a
    /// consistent view, then the transaction that is always rolled back.
    /// </summary>
    private async Task BeginSnapshotTransactionAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, "SET XACT_ABORT ON;", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "SET TRANSACTION ISOLATION LEVEL SNAPSHOT;", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "BEGIN TRANSACTION;", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs a fixed, provider-controlled (never user-supplied) statement.</summary>
    private static async Task ExecuteNonQueryAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Best-effort ROLLBACK; the connection is disposed immediately afterwards.</summary>
    private static async Task RollbackBestEffortAsync(SqlConnection connection)
    {
        try
        {
            if (connection.State == ConnectionState.Open)
            {
                await using var command = new SqlCommand("ROLLBACK TRANSACTION;", connection)
                {
                    CommandTimeout = 5
                };
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Best effort only: the session ends with the connection.
        }
    }

    /// <summary>
    /// Builds the connection from configuration. The connection string is
    /// only ever used internally and is never logged or thrown to callers.
    /// </summary>
    private SqlConnection CreateConnection()
    {
        var builder = new SqlConnectionStringBuilder(_options.ConnectionString)
        {
            ConnectTimeout = Math.Max(1, _options.ConnectionTimeoutSeconds),
            ApplicationName = string.IsNullOrWhiteSpace(_options.ApplicationName) ? "SqlOptimizer" : _options.ApplicationName
        };

        return new SqlConnection(builder.ConnectionString);
    }
}