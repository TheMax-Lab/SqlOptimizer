using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Application.Services.Validation;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Parsing;

namespace SqlOptimizer.Infrastructure.Validation;

/// <summary>
/// SQL Server implementation of <see cref="IDatabaseValidationProvider"/>.
/// Executes the original and candidate read-only and compares their results.
/// Safety is enforced by code, never by convention:
/// <list type="bullet">
/// <item>Both statements must pass the <see cref="TSqlReadOnlyGuard"/> (single
/// SELECT, no DML/DDL/EXEC/transactions, no SELECT INTO, no OPENROWSET/OPENDATASOURCE,
/// no table-valued functions, no linked-server references) before anything runs.</item>
/// <item>Statements referencing parameters are not executed: the current
/// architecture has no parameter values and values are never fabricated.</item>
/// <item>Both statements run inside a single read-only SNAPSHOT-isolation
/// transaction that is always rolled back, so the two executions observe a
/// consistent snapshot and no state change (even from a side-effecting
/// scalar function) is ever committed. The target database must have
/// <c>ALLOW_SNAPSHOT_ISOLATION</c> enabled; otherwise the comparison is not
/// attempted.</item>
/// <item>Every statement gets a command timeout; each result set is bounded
/// server-side (<c>SET ROWCOUNT</c>), by the per-request/server row cap and
/// by the result-set cell cap, and read with streaming readers.</item>
/// <item>Any failure (connection, authentication, timeout, cancellation,
/// SQL error, missing object) yields null ("no evidence"), never a result
/// and never an exception other than cancellation.</item>
/// </list>
/// The provider never logs SQL text, parameter values or connection
/// details. It returns null instead of fabricating a
/// <see cref="DatabaseComparisonResult"/>.
/// </summary>
public sealed class SqlDatabaseValidationProvider : IDatabaseValidationProvider
{
    /// <summary>SQL Server error number raised when snapshot isolation is not enabled on the database.</summary>
    private const int SqlErrorSnapshotIsolationNotEnabled = 3960;

    private readonly ISqlParser _parser;
    private readonly DatabaseOptions _options;
    private readonly ILogger<SqlDatabaseValidationProvider> _logger;
    private readonly Func<SqlConnection> _connectionFactory;

    /// <summary>
    /// Creates a new provider.
    /// </summary>
    /// <param name="parser">The SQL Server parser (used by the read-only guard and for ORDER BY/parameter detection).</param>
    /// <param name="options">Database options (connection and limits).</param>
    /// <param name="logger">Logger (never receives SQL text, parameters or connection details).</param>
    /// <param name="connectionFactory">
    /// Optional connection factory seam (testability): when null the
    /// connection is built from the configured connection string, exactly as
    /// in production. Tests can supply a probe to verify that unsafe
    /// statements never open a connection.
    /// </param>
    public SqlDatabaseValidationProvider(
        ISqlParser parser,
        DatabaseOptions options,
        ILogger<SqlDatabaseValidationProvider> logger,
        Func<SqlConnection>? connectionFactory = null)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionFactory = connectionFactory ?? CreateConnection;
    }

    /// <inheritdoc />
    public SqlDialect Dialect => SqlDialect.SqlServer;

    /// <inheritdoc />
    public bool IsAvailable => _options.Enabled && !string.IsNullOrWhiteSpace(_options.ConnectionString);

    /// <inheritdoc />
    public async Task<DatabaseComparisonResult?> CompareResultsAsync(
        string originalSql,
        string candidateSql,
        int maxRowsForComparison,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            _logger.LogInformation("Result comparison requested but no validation database is configured; no evidence produced.");
            return null;
        }

        var original = TSqlReadOnlyGuard.Check(originalSql, _parser);
        if (!original.IsReadOnly)
        {
            _logger.LogWarning("Result comparison refused for the original statement: {Reason}", original.Reason);
            return null;
        }

        var candidate = TSqlReadOnlyGuard.Check(candidateSql, _parser);
        if (!candidate.IsReadOnly)
        {
            _logger.LogWarning("Result comparison refused for the candidate statement: {Reason}", candidate.Reason);
            return null;
        }

        if (HasParameters(original.Statement!) || HasParameters(candidate.Statement!))
        {
            _logger.LogWarning("Result comparison skipped: the statement references parameters and no parameter values are available for runtime comparison.");
            return null;
        }

        var originalOrders = original.Statement!.OrderBy.Count > 0;
        var candidateOrders = candidate.Statement!.OrderBy.Count > 0;
        if (originalOrders != candidateOrders)
        {
            _logger.LogWarning("Result comparison skipped: ORDER BY presence differs between original and candidate; ordering is externally observable and equivalence cannot be established.");
            return null;
        }

        var cap = Math.Max(1, Math.Min(Math.Max(1, maxRowsForComparison), Math.Max(1, _options.MaxRowsForComparison)));
        var fetchLimit = cap + 1;

        _logger.LogInformation(
            "Database result comparison started (cap: {Cap} rows, positional: {Positional}).",
            cap,
            originalOrders);

        using var connection = _connectionFactory();
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
                "Result comparison not performed: snapshot isolation is not enabled on the validation database (SQL error {ErrorCode}); a consistent snapshot between the two executions cannot be guaranteed. Enable ALLOW_SNAPSHOT_ISOLATION.",
                ex.Number);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Result comparison not performed: the validation database connection failed.");
            return null;
        }

        try
        {
            var originalStopwatch = Stopwatch.StartNew();
            var (originalColumns, originalRows) = await ReadResultSetAsync(connection, originalSql, fetchLimit, cancellationToken).ConfigureAwait(false);
            originalStopwatch.Stop();

            var candidateStopwatch = Stopwatch.StartNew();
            var (candidateColumns, candidateRows) = await ReadResultSetAsync(connection, candidateSql, fetchLimit, cancellationToken).ConfigureAwait(false);
            candidateStopwatch.Stop();

            await RollbackBestEffortAsync(connection).ConfigureAwait(false);

            var outcome = SqlResultComparator.Compare(
                originalColumns, originalRows, candidateColumns, candidateRows, originalOrders, cap);

            _logger.LogInformation(
                "Database result comparison completed (original: {OriginalRows} rows in {OriginalMs} ms, candidate: {CandidateRows} rows in {CandidateMs} ms, equal: {Equal}, truncated: {Truncated}).",
                Math.Min(originalRows.Length, cap), originalStopwatch.ElapsedMilliseconds,
                Math.Min(candidateRows.Length, cap), candidateStopwatch.ElapsedMilliseconds,
                outcome.ResultsEqual, outcome.Truncated);

            return new DatabaseComparisonResult
            {
                ResultsEqual = outcome.ResultsEqual,
                OriginalRows = Math.Min(originalRows.Length, cap),
                CandidateRows = Math.Min(candidateRows.Length, cap),
                Truncated = outcome.Truncated,
                OriginalDuration = originalStopwatch.Elapsed,
                CandidateDuration = candidateStopwatch.Elapsed,
                Notes = outcome.Notes
            };
        }
        catch (OperationCanceledException)
        {
            await RollbackBestEffortAsync(connection).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Result comparison failed during execution (SQL error {ErrorCode}); no evidence produced.", (ex as SqlException)?.Number ?? 0);
            await RollbackBestEffortAsync(connection).ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>
    /// Reads one result set with streaming access, bounded server-side by
    /// <c>SET ROWCOUNT</c> and client-side by the row/cell caps. Never loads
    /// an unbounded result set into memory.
    /// </summary>
    private async Task<(IReadOnlyList<SqlResultComparator.ResultColumn> Columns, object?[][] Rows)> ReadResultSetAsync(
        SqlConnection connection,
        string sql,
        int fetchLimit,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand($"SET ROWCOUNT {fetchLimit};\n{sql}", connection)
        {
            CommandTimeout = Math.Max(1, _options.CommandTimeoutSeconds)
        };

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var columns = new List<SqlResultComparator.ResultColumn>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(new SqlResultComparator.ResultColumn(reader.GetName(i), reader.GetDataTypeName(i)));
        }

        var rows = new List<object?[]>(fetchLimit);
        var cells = 0;
        while (rows.Count < fetchLimit)
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                break;
            }

            var row = new object?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                cells++;
            }

            rows.Add(row);

            if (cells >= Math.Max(1, _options.MaxResultCells))
            {
                // Cell cap reached: stop materializing (truncation is
                // reported by the comparator via the row cap).
                break;
            }
        }

        return (columns, [.. rows]);
    }

    /// <summary>
    /// Configures the session for a consistent, non-committed comparison:
    /// XACT_ABORT so errors never leave a half-open transaction, SNAPSHOT
    /// isolation so both executions observe the same snapshot, then begins
    /// the transaction that wraps both executions.
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

    /// <summary>True when the statement references parameters (no values are available for execution).</summary>
    private static bool HasParameters(SelectStatement statement) =>
        QueryStructureSnapshot.Build(statement).Parameters.Count > 0;
}