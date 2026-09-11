using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Infrastructure.Parsing;

namespace SqlOptimizer.Rules.Tests.TestSupport;

/// <summary>
/// Builds real analysis contexts from SQL text using the production SQL
/// Server parser, so rule tests exercise the exact AST shape the pipeline
/// produces.
/// </summary>
public static class RuleTestHelper
{
    /// <summary>Parses SQL and builds an analysis context.</summary>
    /// <param name="sql">The SQL text.</param>
    /// <param name="schema">Optional schema metadata.</param>
    /// <param name="options">Optional rule threshold options.</param>
    public static SqlAnalysisContext BuildContext(
        string sql,
        DatabaseSchema? schema = null,
        RuleOptions? options = null)
    {
        var parser = new SqlServerSqlParser();
        var parsed = parser.Parse(sql);

        return new SqlAnalysisContext
        {
            Ast = parsed.Root,
            Dialect = SqlDialect.SqlServer,
            Schema = schema,
            Options = options ?? new RuleOptions(),
        };
    }

    /// <summary>Runs a single rule against a SQL text and returns findings.</summary>
    public static IReadOnlyList<SqlFinding> Run<TRule>(
        string sql,
        DatabaseSchema? schema = null,
        RuleOptions? options = null)
        where TRule : ISqlOptimizationRule, new() =>
        new TRule().Analyze(BuildContext(sql, schema, options)).ToList();

    /// <summary>Builds a schema with a single table for metadata rules.</summary>
    public static DatabaseSchema BuildSchema(
        params (string Name, string DataType, bool Nullable, bool PrimaryKey)[] columns) =>
        new([new DatabaseTable(
            "dbo",
            "T",
            null,
            columns.Select(c => new DatabaseColumn(c.Name, c.DataType, c.Nullable, c.PrimaryKey)).ToList(),
            []
        )]);
}
