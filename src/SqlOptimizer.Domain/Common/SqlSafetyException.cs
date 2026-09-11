namespace SqlOptimizer.Domain.Common;

/// <summary>
/// Thrown when the SQL safety guard rejects a statement (for example DML or DDL
/// submitted for validation/execution). Maps to the <c>SQL_INVALID_INPUT</c>
/// API error code.
/// </summary>
public sealed class SqlSafetyException : SqlOptimizerException
{
    /// <summary>Creates a new instance with the specified message.</summary>
    /// <param name="message">Description of the rejected statement.</param>
    public SqlSafetyException(string message)
        : base(message)
    {
    }
}
