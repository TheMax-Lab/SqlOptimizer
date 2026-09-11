using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Domain.Metadata;

namespace SqlOptimizer.Infrastructure.Validation;

/// <summary>
/// SQL Server implementation of <see cref="IDatabaseMetadataProvider"/>:
/// reads tables, columns (name, order, data type, nullability), primary
/// keys, indexes and row-count estimates from the system catalog, only for
/// the tables that are requested. All access is read-only and bounded.
/// Metadata is cached per table in a bounded, TTL-based, thread-safe
/// in-process cache; a fetch failure invalidates the attempt (null) rather
/// than serving unbounded-staleness data. The provider never throws for
/// operational failures, never fabricates metadata and never logs
/// connection details.
/// </summary>
public sealed class SqlDatabaseMetadataProvider : IDatabaseMetadataProvider
{
    /// <summary>Safe limit on the number of distinct tables fetched per call.</summary>
    private const int MaxTablesPerFetch = 50;

    /// <summary>Safe limit on the number of tables fetched for a full-schema read.</summary>
    private const int MaxTablesForFullSchema = 500;

    private readonly DatabaseOptions _options;
    private readonly ILogger<SqlDatabaseMetadataProvider> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct CacheEntry(DatabaseTable Table, DateTimeOffset Expires);

    /// <summary>
    /// Creates a new provider.
    /// </summary>
    /// <param name="options">Database options (connection and cache limits).</param>
    /// <param name="logger">Logger (never receives connection details).</param>
    public SqlDatabaseMetadataProvider(DatabaseOptions options, ILogger<SqlDatabaseMetadataProvider> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsAvailable => _options.Enabled && !string.IsNullOrWhiteSpace(_options.ConnectionString);

    /// <inheritdoc />
    public async Task<DatabaseSchema?> GetTablesSchemaAsync(
        IReadOnlyCollection<string> tableNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tableNames);
        if (!IsAvailable)
        {
            _logger.LogInformation("Live schema metadata requested but no database is configured; no metadata produced.");
            return null;
        }

        var requested = tableNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requested.Count == 0)
        {
            return DatabaseSchema.Empty;
        }

        if (requested.Count > MaxTablesPerFetch)
        {
            _logger.LogWarning("Live schema metadata request limited to {Max} of {Total} tables.", MaxTablesPerFetch, requested.Count);
            requested = requested.Take(MaxTablesPerFetch).ToList();
        }

        var tables = new List<DatabaseTable>(requested.Count);
        var missing = new List<(string Schema, string Name)>();

        foreach (var name in requested)
        {
            var (schema, tableName) = SplitTableName(name);
            var key = string.IsNullOrEmpty(schema) ? tableName : $"{schema}.{tableName}";

            if (_cache.TryGetValue(key, out var entry) && entry.Expires > DateTimeOffset.UtcNow)
            {
                tables.Add(entry.Table);
            }
            else
            {
                missing.Add((schema, tableName));
            }
        }

        if (missing.Count > 0)
        {
            var fetched = await FetchTablesAsync(missing, cancellationToken).ConfigureAwait(false);
            if (fetched is null)
            {
                // A hard failure while a fresh read was required: serving a
                // partially stale view could silently create unsafe
                // validation conclusions, so no metadata is produced at all.
                return null;
            }

            tables.AddRange(fetched);
        }

        _logger.LogDebug("Live schema metadata resolved for {Count} table(s).", tables.Count);
        return new DatabaseSchema(tables);
    }

    /// <summary>
    /// Reads the schema metadata of every user table in the current database
    /// (bounded by <see cref="MaxTablesForFullSchema"/>). Reuses the same
    /// catalog queries, caching and failure semantics as
    /// <see cref="GetTablesSchemaAsync"/>: returns null when the database is
    /// not configured or a read failed, and an empty schema when the database
    /// has no user tables.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<DatabaseSchema?> GetFullSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            _logger.LogInformation("Full schema metadata requested but no database is configured; no metadata produced.");
            return null;
        }

        try
        {
            using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var tables = await DiscoverTablesAsync(connection, cancellationToken).ConfigureAwait(false);
            if (tables.Count == 0)
            {
                _logger.LogDebug("Full schema metadata resolved: the database has no user tables.");
                return DatabaseSchema.Empty;
            }

            var columns = await ReadColumnsAsync(connection, tables, cancellationToken).ConfigureAwait(false);
            var indexes = await ReadIndexesAsync(connection, tables, cancellationToken).ConfigureAwait(false);
            var rowCounts = await ReadRowCountsAsync(connection, tables, cancellationToken).ConfigureAwait(false);

            var result = Assemble(columns, indexes, rowCounts);
            foreach (var table in result)
            {
                PutInCache(TableKey(table.Schema, table.Name), table);
            }

            _logger.LogDebug("Full schema metadata resolved for {Count} table(s).", result.Count);
            return new DatabaseSchema(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Full schema metadata could not be read from the database; no metadata produced.");
            return null;
        }
    }

    /// <summary>
    /// Lists the user tables of the current database (bounded, parameterized,
    /// deterministic order).
    /// </summary>
    private async Task<IReadOnlyList<(string Schema, string Name)>> DiscoverTablesAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string Sql = """
            SELECT TOP (@max)
                SCHEMA_NAME(t.schema_id) AS sch,
                t.name AS tab
            FROM sys.tables t
            ORDER BY t.schema_id, t.name
            """;

        using var command = new SqlCommand(Sql, connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.Add("@max", SqlDbType.Int).Value = MaxTablesForFullSchema;

        var tables = new List<(string, string)>(MaxTablesForFullSchema);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tables.Add((reader.GetString(0), reader.GetString(1)));
        }

        if (tables.Count >= MaxTablesForFullSchema)
        {
            _logger.LogWarning("Full schema metadata request limited to {Max} tables.", MaxTablesForFullSchema);
        }

        return tables;
    }

    /// <summary>
    /// Reads the catalog for the missing tables in one connection round trip
    /// (columns, indexes, row-count estimates) and caches the results.
    /// Returns null on any operational failure.
    /// </summary>
    private async Task<IReadOnlyList<DatabaseTable>?> FetchTablesAsync(
        IReadOnlyList<(string Schema, string Name)> tables,
        CancellationToken cancellationToken)
    {
        try
        {
            using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var columns = await ReadColumnsAsync(connection, tables, cancellationToken).ConfigureAwait(false);
            var indexes = await ReadIndexesAsync(connection, tables, cancellationToken).ConfigureAwait(false);
            var rowCounts = await ReadRowCountsAsync(connection, tables, cancellationToken).ConfigureAwait(false);

            var result = Assemble(columns, indexes, rowCounts);
            foreach (var table in result)
            {
                PutInCache(TableKey(table.Schema, table.Name), table);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Live schema metadata could not be read from the database; no metadata produced.");
            return null;
        }
    }

    /// <summary>Reads column metadata (name, order, type, nullability, primary-key flag) for the requested tables.</summary>
    private static async Task<IReadOnlyList<(string Sch, string Tab, string Col, int Ord, bool Nullable, string Type, bool Pk)>> ReadColumnsAsync(
        SqlConnection connection,
        IReadOnlyList<(string Schema, string Name)> tables,
        CancellationToken cancellationToken)
    {
        const string Sql = """
            SELECT
                SCHEMA_NAME(t.schema_id) AS [sch],
                t.name AS [tab],
                c.name AS [col],
                c.column_id AS [ord],
                c.is_nullable AS [nullab],
                CASE
                    WHEN TYPE_NAME(c.user_type_id) IN ('char', 'varchar', 'binary', 'varbinary')
                        THEN TYPE_NAME(c.user_type_id) + N'(' + CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length AS NVARCHAR(10)) END + N')'
                    WHEN TYPE_NAME(c.user_type_id) IN ('nchar', 'nvarchar')
                        THEN N'nvarchar(' + CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length / 2 AS NVARCHAR(10)) END + N')'
                    WHEN TYPE_NAME(c.user_type_id) IN ('decimal', 'numeric')
                        THEN N'decimal(' + CAST(c.precision AS NVARCHAR(3)) + N',' + CAST(c.scale AS NVARCHAR(3)) + N')'
                    WHEN TYPE_NAME(c.user_type_id) IN ('time', 'datetime2', 'datetimeoffset')
                        THEN TYPE_NAME(c.user_type_id) + N'(' + CAST(c.scale AS NVARCHAR(1)) + N')'
                    ELSE TYPE_NAME(c.user_type_id)
                END AS [type],
                CASE WHEN (
                    SELECT COUNT(*)
                    FROM sys.indexes i
                    JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.column_id = c.column_id
                    WHERE i.object_id = t.object_id AND i.is_primary_key = 1
                ) > 0 THEN 1 ELSE 0 END AS [pk]
            FROM sys.tables t
            JOIN sys.columns c ON c.object_id = t.object_id
            WHERE
            """;

        using var command = new SqlCommand(Sql, connection)
        {
            CommandTimeout = 30
        };

        command.CommandText += BuildTableFilter(tables, command) + "\nORDER BY sch, tab, ord";

        var result = new List<(string, string, string, int, bool, string, bool)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetBoolean(4),
                reader.GetString(5),
                reader.GetInt32(6) == 1));
        }

        return result;
    }

    /// <summary>Reads index metadata (name, key/included columns, uniqueness, clustered flag) for the requested tables.</summary>
    private static async Task<IReadOnlyList<(string Sch, string Tab, string Index, bool Unique, bool Clustered, string Column, bool IsKey, int Ord)>> ReadIndexesAsync(
        SqlConnection connection,
        IReadOnlyList<(string Schema, string Name)> tables,
        CancellationToken cancellationToken)
    {
        const string Sql = """
            SELECT
                SCHEMA_NAME(t.schema_id) AS [sch],
                t.name AS [tab],
                i.name AS [idx],
                i.is_unique AS [unique_flag],
                CASE WHEN i.type = 1 THEN 1 ELSE 0 END AS [clustered],
                c.name AS [col],
                CASE WHEN ic.key_ordinal > 0 THEN 1 ELSE 0 END AS [is_key],
                ic.index_column_id AS [ord]
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = i.object_id AND c.column_id = ic.column_id
            WHERE i.type IN (0, 1, 2, 3, 5, 6, 7, 8)
              AND
            """;

        using var command = new SqlCommand(Sql, connection)
        {
            CommandTimeout = 30
        };

        command.CommandText += BuildTableFilter(tables, command) + "\nORDER BY sch, tab, idx, ic.index_column_id";

        var result = new List<(string, string, string, bool, bool, string, bool, int)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetInt32(4) == 1,
                reader.GetString(5),
                reader.GetInt32(6) == 1,
                reader.GetInt32(7)));
        }

        return result;
    }

    /// <summary>Reads estimated row counts (index row estimates; safe and cheap) for the requested tables.</summary>
    private static async Task<IReadOnlyDictionary<(string Sch, string Tab), long>> ReadRowCountsAsync(
        SqlConnection connection,
        IReadOnlyList<(string Schema, string Name)> tables,
        CancellationToken cancellationToken)
    {
        // sys.sysindexes (indid 0 = heap, 1 = clustered index) provides the
        // per-index row estimate without scanning the table. The legacy view
        // is used instead of sys.dm_db_partition_rows because it is available
        // on every SQL Server-compatible engine the provider must support.
        const string Sql = """
            SELECT
                SCHEMA_NAME(t.schema_id) AS [sch],
                t.name AS [tab],
                SUM(i.rows) AS [rc]
            FROM sys.sysindexes i
            JOIN sys.tables t ON t.object_id = i.id
            WHERE i.indid IN (0, 1)
              AND
            """;

        using var command = new SqlCommand(Sql, connection)
        {
            CommandTimeout = 30
        };

        command.CommandText += BuildTableFilter(tables, command) + "\nGROUP BY SCHEMA_NAME(t.schema_id), t.name";

        var result = new Dictionary<(string, string), long>(TableKeyComparer);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // Convert.ToInt64 (not GetInt64): a SQL Server-compatible engine may return
            // SUM(i.rows) as int, on which GetInt64 would throw InvalidCastException.
            result[(reader.GetString(0), reader.GetString(1))] = Convert.ToInt64(reader.GetValue(2));
        }

        return result;
    }

    /// <summary>
    /// Builds the parameterized table filter (a VALUES list of
    /// schema/name pairs) appended to the catalog queries and registers the
    /// parameters. The alias <c>t</c> must refer to <c>sys.tables</c>.
    /// </summary>
    private static string BuildTableFilter(
        IReadOnlyList<(string Schema, string Name)> tables,
        SqlCommand command)
    {
        var values = new List<string>(tables.Count);
        for (var i = 0; i < tables.Count; i++)
        {
            values.Add($"(@s{i}, @n{i})");
            command.Parameters.AddWithValue($"@s{i}", tables[i].Schema);
            command.Parameters.AddWithValue($"@n{i}", tables[i].Name);
        }

        // A leading space separates the EXISTS predicate from the base
        // statement it is appended to, which ends in "WHERE" or "AND"
        // (e.g. "...WHERE EXISTS ..." / "...AND EXISTS ..."). Without the
        // separator the concatenated SQL becomes malformed ("WHEREEXISTS" /
        // "ANDEXISTS") and every catalog query fails.
        return " EXISTS (SELECT 1 FROM (VALUES "
            + string.Join(", ", values)
            + ") AS v(sch, tab) WHERE v.tab = t.name AND (v.sch = N'' OR v.sch = SCHEMA_NAME(t.schema_id)))";
    }

    /// <summary>Assembles the catalog rows into immutable table metadata.</summary>
    private static IReadOnlyList<DatabaseTable> Assemble(
        IReadOnlyList<(string Sch, string Tab, string Col, int Ord, bool Nullable, string Type, bool Pk)> columns,
        IReadOnlyList<(string Sch, string Tab, string Index, bool Unique, bool Clustered, string Column, bool IsKey, int Ord)> indexes,
        IReadOnlyDictionary<(string Sch, string Tab), long> rowCounts)
    {
        var groups = new Dictionary<string, TableGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            var key = TableKey(column.Sch, column.Tab);
            if (!groups.TryGetValue(key, out var group))
            {
                group = new TableGroup(column.Sch, column.Tab);
                groups[key] = group;
            }

            group.Columns.Add(new DatabaseColumn(column.Col, column.Type, column.Nullable, column.Pk));
        }

        foreach (var index in indexes)
        {
            var key = TableKey(index.Sch, index.Tab);
            if (groups.TryGetValue(key, out var group))
            {
                group.IndexRows.Add((index.Index, index.Unique, index.Clustered, index.Column, index.IsKey, index.Ord));
            }
        }

        foreach (var group in groups.Values)
        {
            group.RowCount = rowCounts.GetValueOrDefault((group.Sch, group.Tab));
        }

        return groups.Values
            .OrderBy(group => group.Sch, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Tab, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.ToTable())
            .ToList();
    }

    /// <summary>Bounded, TTL-based, thread-safe metadata cache insert.</summary>
    private void PutInCache(string key, DatabaseTable table)
    {
        var max = Math.Max(1, _options.MetadataCacheMaxEntries);
        if (_cache.Count >= max)
        {
            EvictExpiredAndOldest(max);
        }

        var ttl = TimeSpan.FromSeconds(Math.Max(1, _options.MetadataCacheTtlSeconds));
        _cache[key] = new CacheEntry(table, DateTimeOffset.UtcNow.Add(ttl));
    }

    /// <summary>Evicts expired entries, then the soonest-expiring ones until under the bound.</summary>
    private void EvictExpiredAndOldest(int max)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, entry) in _cache)
        {
            if (entry.Expires <= now)
            {
                _cache.TryRemove(key, out _);
            }
        }

        var safety = 0;
        while (_cache.Count >= max && safety < max + 1)
        {
            string? oldestKey = null;
            var oldest = DateTimeOffset.MaxValue;
            foreach (var (key, entry) in _cache)
            {
                if (entry.Expires < oldest)
                {
                    oldest = entry.Expires;
                    oldestKey = key;
                }
            }

            if (oldestKey is null)
            {
                break;
            }

            _cache.TryRemove(oldestKey, out _);
            safety++;
        }
    }

    /// <summary>Cache key of a table (schema-qualified when known).</summary>
    private static string TableKey(string schema, string name) =>
        string.IsNullOrEmpty(schema) ? name : $"{schema}.{name}";

    /// <summary>Case-insensitive comparer for (schema, table) keys.</summary>
    private static readonly IEqualityComparer<(string Sch, string Tab)> TableKeyComparer = new Comparer();

    /// <summary>Splits a (possibly qualified) table name into schema and name.</summary>
    private static (string Schema, string Name) SplitTableName(string name)
    {
        var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => (string.Empty, name),
            1 => (string.Empty, parts[0]),
            2 => (parts[0], parts[1]),
            _ => (parts[^2], parts[^1])
        };
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

    /// <summary>Accumulator for one table while assembling catalog rows.</summary>
    private sealed class TableGroup
    {
        public TableGroup(string schema, string name)
        {
            Sch = schema;
            Tab = name;
        }

        public string Sch { get; }

        public string Tab { get; }

        public List<DatabaseColumn> Columns { get; } = [];

        public List<(string Index, bool Unique, bool Clustered, string Column, bool IsKey, int Ord)> IndexRows { get; } = [];

        public long? RowCount { get; set; }

        /// <summary>Builds the immutable table metadata (deterministic index order).</summary>
        public DatabaseTable ToTable()
        {
            var indexes = IndexRows
                .GroupBy(row => row.Index, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var meta = group.First();
                    var keyColumns = group.Where(row => row.IsKey).OrderBy(row => row.Ord).Select(row => row.Column).ToList();
                    var includedColumns = group.Where(row => !row.IsKey).OrderBy(row => row.Ord).Select(row => row.Column).ToList();
                    return new DatabaseIndex(group.Key, keyColumns, includedColumns, meta.Unique, meta.Clustered);
                })
                .ToList();

            return new DatabaseTable(Sch, Tab, RowCount, [.. Columns], indexes);
        }
    }

    /// <summary>Case-insensitive comparison of (schema, table) tuples.</summary>
    private sealed class Comparer : IEqualityComparer<(string Sch, string Tab)>
    {
        public bool Equals((string Sch, string Tab) a, (string Sch, string Tab) b) =>
            string.Equals(a.Sch, b.Sch, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Tab, b.Tab, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Sch, string Tab) obj) =>
            HashCode.Combine(
                string.Equals(obj.Sch, string.Empty, StringComparison.Ordinal) ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Sch),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Tab));
    }
}