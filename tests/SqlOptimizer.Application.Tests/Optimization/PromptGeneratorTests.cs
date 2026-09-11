using FluentAssertions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Application.Services;
using SqlOptimizer.Domain.AST;
using SqlOptimizer.Domain.Analysis;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Domain.Metadata;
using SqlOptimizer.Domain.Rules;
using SqlOptimizer.Infrastructure.Parsing;
using Xunit;

namespace SqlOptimizer.Application.Tests.Optimization;

/// <summary>
/// Prompt generation: the system prompt must instruct the LLM that it
/// generates (not validates) candidates, must preserve semantics, must not
/// invent facts, must return the strict structured format and must prefer
/// evidence-backed conservative rewrites. The user prompt must carry the
/// deterministic evidence (query, findings, scores, schema, plan).
/// </summary>
public class PromptGeneratorTests
{
    private readonly PromptGenerator _generator = new();

    [Fact]
    public void SystemPrompt_StatesCandidatesAreNotValidation()
    {
        var (_, context) = BuildContext("SELECT * FROM Customers");
        var prompt = _generator.Generate(context.Analysis, context);

        prompt.SystemPrompt.Should().Contain("CANDIDATES");
        prompt.SystemPrompt.Should().Contain("you do NOT validate them");
        prompt.SystemPrompt.Should().Contain("independently");
        prompt.SystemPrompt.Should().Contain("validation decides semantic safety");
    }

    [Fact]
    public void SystemPrompt_StatesSemanticPreservationAndNoInventedFacts()
    {
        var (_, context) = BuildContext("SELECT * FROM Customers");
        var prompt = _generator.Generate(context.Analysis, context);

        prompt.SystemPrompt.Should().Contain("Preserve semantics exactly");
        prompt.SystemPrompt.Should().Contain("NULLs, duplicates, ordering, aggregation, joins or predicates");
        prompt.SystemPrompt.Should().Contain("Never invent");
        prompt.SystemPrompt.Should().Contain("merely because it looks faster");
        prompt.SystemPrompt.Should().Contain("conservative");
    }

    [Fact]
    public void SystemPrompt_RequiresStrictStructuredFormat()
    {
        var (_, context) = BuildContext("SELECT * FROM Customers");
        var prompt = _generator.Generate(context.Analysis, context);

        prompt.SystemPrompt.Should().Contain("\"candidates\"");
        prompt.SystemPrompt.Should().Contain("\"sql\"");
        prompt.SystemPrompt.Should().Contain("\"expectedImpact\"");
        prompt.SystemPrompt.Should().Contain("\"confidence\"");
        prompt.SystemPrompt.Should().Contain("no markdown fences");
    }

    [Fact]
    public void UserPrompt_ContainsQueryFindingsAndStrategy()
    {
        var (_, context) = BuildContext("SELECT * FROM Customers WHERE UPPER(City) = 'Rome'");
        var prompt = _generator.Generate(context.Analysis, context);

        prompt.UserPrompt.Should().Contain("SELECT * FROM Customers");
        prompt.UserPrompt.Should().Contain("SQL001");
        prompt.UserPrompt.Should().Contain("Strategy:");
        prompt.UserPrompt.Should().Contain("## Original query");
    }

    [Fact]
    public void UserPrompt_IncludesSchemaSectionWhenSchemaProvided()
    {
        var schema = OptimizationTestSupport.TestSchema();
        var (_, context) = BuildContext("SELECT * FROM Customers", schema);
        var prompt = _generator.Generate(context.Analysis, context);

        prompt.UserPrompt.Should().Contain("## Schema metadata");
        prompt.UserPrompt.Should().Contain("Customers");
    }

    [Fact]
    public void SystemPrompt_RespectsMaxCandidatesOption()
    {
        var (_, context) = BuildContext("SELECT * FROM Customers");
        context = context with { Options = context.Options with { MaxCandidates = 5 } };
        var prompt = _generator.Generate(context.Analysis, context);

        prompt.SystemPrompt.Should().Contain("at most 5 candidates");
    }

    /// <summary>Parses the query, runs the real rules and builds the context DTO.</summary>
    private static (SqlAnalysis, SqlOptimizationContext) BuildContext(string sql, DatabaseSchema? schema = null)
    {
        var parser = new SqlServerSqlParser();
        var parsed = parser.Parse(sql);
        var findings = OptimizationTestSupport.CreateRuleRegistry().Analyze(new SqlAnalysisContext
        {
            Ast = parsed.Root,
            Dialect = SqlDialect.SqlServer,
            Schema = schema
        });

        var analysis = new SqlAnalysis
        {
            Sql = sql,
            Dialect = SqlDialect.SqlServer,
            Ast = parsed.Root,
            ComplexityScore = 10,
            PerformanceScore = 5,
            Findings = findings,
            Statistics = QueryStatistics.FromAst(parsed.Root)
        };

        var context = new SqlOptimizationContext(
            sql,
            SqlDialect.SqlServer,
            analysis,
            schema,
            null,
            new OptimizationOptions(),
            new Domain.Optimization.OptimizationPlan([]),
            []);

        return (analysis, context);
    }
}
