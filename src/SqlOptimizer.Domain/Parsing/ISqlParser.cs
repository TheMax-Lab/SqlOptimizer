using SqlOptimizer.Domain.Common;

namespace SqlOptimizer.Domain.Parsing;

/// <summary>
/// Abstraction over a concrete SQL parser. Implementations must produce the
/// parser independent AST defined in <c>SqlOptimizer.Domain.AST</c>. The
/// pipeline supports a single dialect per implementation; dialects that are
/// not implemented must throw <see cref="SqlUnsupportedDialectException"/>
/// when selected.
/// </summary>
public interface ISqlParser
{
    /// <summary>The dialect this parser implements.</summary>
    SqlDialect Dialect { get; }

    /// <summary>
    /// Parses a SQL statement into the SqlOptimizer AST.
    /// </summary>
    /// <param name="sql">The SQL text to parse.</param>
    /// <exception cref="SqlParseException">The SQL cannot be parsed.</exception>
    /// <exception cref="SqlInvalidInputException">The SQL is empty or not a SELECT statement.</exception>
    ParsedQuery Parse(string sql);
}
