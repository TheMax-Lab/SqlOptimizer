namespace SqlOptimizer.Domain.Common;

/// <summary>
/// Identifies the SQL dialect of a query. Only <see cref="SqlServer"/> is fully
/// implemented in the MVP; the other values are reserved for future parser
/// implementations and are rejected by the analysis pipeline.
/// </summary>
public enum SqlDialect
{
    /// <summary>SQL Server (Transact-SQL).</summary>
    SqlServer,

    /// <summary>PostgreSQL (reserved, not implemented in MVP).</summary>
    PostgreSql,

    /// <summary>MySQL (reserved, not implemented in MVP).</summary>
    MySql,

    /// <summary>Oracle (reserved, not implemented in MVP).</summary>
    Oracle
}
