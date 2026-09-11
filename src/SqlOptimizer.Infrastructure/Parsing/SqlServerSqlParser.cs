using System.IO;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Parsing;
using Sdom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlOptimizer.Infrastructure.Parsing;

/// <summary>
/// SQL Server T-SQL parser based on
/// <see href="https://learn.microsoft.com/dotnet/api/microsoft.sqlserver.transactsql.scriptdom">Microsoft.SqlServer.TransactSql.ScriptDom</see>.
/// Parses a single SELECT statement into the parser independent
/// SqlOptimizer domain AST. DML/DDL, multiple statements and empty input
/// are rejected with typed exceptions.
/// </summary>
public sealed class SqlServerSqlParser : ISqlParser
{
    /// <inheritdoc />
    public SqlDialect Dialect => SqlDialect.SqlServer;

    /// <inheritdoc />
    public ParsedQuery Parse(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new SqlInvalidInputException("The SQL text must not be empty.");
        }

        var script = ParseScript(sql);
        var statements = script.Batches
            .SelectMany(batch => batch.Statements)
            .ToList();

        if (statements.Count == 0)
        {
            throw new SqlInvalidInputException(
                "The SQL text does not contain an executable statement.");
        }

        if (statements.Count > 1)
        {
            throw new SqlInvalidInputException(
                $"Only a single SQL statement is supported for analysis; found {statements.Count}.");
        }

        if (statements[0] is not Sdom.SelectStatement select)
        {
            throw new SqlInvalidInputException(
                $"Only SELECT statements are supported for analysis; found '{statements[0].GetType().Name}'.");
        }

        // The converter accumulates per-query non-fatal warnings, so every
        // parse must run on a fresh instance: reusing one across Parse calls
        // would leak warnings from earlier queries into later ones.
        return new ScriptDomToAstConverter().Convert(select);
    }

    /// <summary>
    /// Runs the ScriptDom parser and maps parse errors to
    /// <see cref="SqlParseException"/>.
    /// </summary>
    /// <param name="sql">The SQL text to parse.</param>
    private static Sdom.TSqlScript ParseScript(string sql)
    {
        var parser = Sdom.TSqlParser.CreateParser(Sdom.SqlVersion.Sql160, false);
        Sdom.TSqlFragment fragment;

        try
        {
            fragment = parser.Parse(new StringReader(sql), out IList<Sdom.ParseError> errors);

            if (errors is { Count: > 0 })
            {
                var first = errors[0];
                throw new SqlParseException(
                    $"SQL parse error at line {first.Line}, column {first.Column}: {first.Message}");
            }
        }
        catch (Exception ex) when (ex is not SqlOptimizerException)
        {
            throw new SqlParseException("The SQL statement could not be parsed.", ex);
        }

        return fragment is Sdom.TSqlScript script
            ? script
            : throw new SqlParseException(
                $"The SQL text did not parse to a complete script (got '{fragment?.GetType().Name ?? "nothing"}').");
    }
}