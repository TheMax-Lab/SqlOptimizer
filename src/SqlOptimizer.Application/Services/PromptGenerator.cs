using System.Text;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.DTOs;
using SqlOptimizer.Domain.Analysis;

namespace SqlOptimizer.Application.Services;

/// <summary>
/// Builds a deterministic, self-contained prompt for an LLM optimizer.
/// The generated prompt contains all context (query, findings, plan, indexes,
/// schema, plan excerpt) so the model needs no external access, and a strict
/// output contract so the response can be parsed and validated.
/// </summary>
public sealed class PromptGenerator : IPromptGenerator
{
    private const int MaxSchemaChars = 6000;
    private const int MaxPlanXmlChars = 4000;
    private const int MaxFindingFindings = 25;

    /// <summary>Generates the prompt for the given analysis and optimization context.</summary>
    /// <param name="analysis">The static analysis result.</param>
    /// <param name="context">The optimization context.</param>
    public LlmOptimizationPrompt Generate(SqlAnalysis analysis, SqlOptimizationContext context)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(context);

        return new LlmOptimizationPrompt(
            BuildSystemPrompt(Math.Clamp(context.Options.MaxCandidates, 1, LlmResponseParser.DefaultMaxCandidates)),
            BuildUserPrompt(analysis, context));
    }

    private static string BuildSystemPrompt(int maxCandidates) =>
        "You are a senior SQL Server performance engineer. You generate optimization CANDIDATES for T-SQL " +
        "queries from static analysis. Follow these rules strictly:\n" +
        "1. You generate candidates, you do NOT validate them. Every candidate will be independently " +
        "validated; only that validation decides semantic safety.\n" +
        "2. Preserve semantics exactly: same result rows, same columns, same column order, same names. " +
        "Never change behavior involving NULLs, duplicates, ordering, aggregation, joins or predicates " +
        "unless the supplied findings give sufficient evidence that the change is safe.\n" +
        "3. Never invent indexes, schema facts, constraints, statistics, row counts, execution plans or " +
        "business rules. Work only with the provided context.\n" +
        "4. Do not assume a transformation is safe merely because it looks faster.\n" +
        "5. Prefer conservative optimizations that are directly supported by the supplied deterministic " +
        "findings and recommendations.\n" +
        "6. Rewrite ONLY the given query. Never add, remove or rename tables, columns or parameters. " +
        "Do not add hints (OPTION, LOOP JOIN, ...) and do not modify DML/DDL. If the query is not a " +
        "SELECT, return an empty candidate list.\n" +
        "7. Reply with a SINGLE JSON object, no markdown fences, no comments, exactly this shape:\n" +
        "{\"candidates\":[{\"sql\":\"...\",\"explanation\":\"...\",\"expectedImpact\":\"...\",\"confidence\":0.0," +
        "\"warnings\":[\"...\"],\"assumptions\":[\"...\"]}]}.\n" +
        $"8. Return at most {maxCandidates} candidates. Escape the SQL strings for JSON. \"confidence\" is " +
        "a number between 0 and 1.";

    private string BuildUserPrompt(SqlAnalysis analysis, SqlOptimizationContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Generate optimization candidates for the following T-SQL query for SQL Server.");
        builder.AppendLine();
        builder.AppendLine("## Original query");
        builder.AppendLine("```sql");
        builder.AppendLine(context.OriginalSql.Trim());
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine($"Strategy: {context.Options.Strategy}. " +
            "Keep changes compatible with the strategy: Conservative = low risk only; " +
            "Balanced = low and medium risk; Aggressive = medium and high risk, each clearly warned.");
        AppendScores(builder, analysis);
        AppendFindings(builder, analysis);
        AppendPlan(builder, context);
        AppendIndexes(builder, context);
        AppendSchema(builder, context);
        AppendPlanXml(builder, context);
        return builder.ToString().TrimEnd();
    }

    private static void AppendScores(StringBuilder builder, SqlAnalysis analysis)
    {
        var stats = analysis.Statistics;
        builder.AppendLine();
        builder.AppendLine("## Deterministic analysis (scores: 0 = best, 100 = worst)");
        builder.AppendLine(
            $"complexity: {analysis.ComplexityScore}, performanceRisk: {analysis.PerformanceScore}, " +
            $"tables: {stats.TableCount}, joins: {stats.JoinCount}, subqueries: {stats.SubqueryCount}, " +
            $"maxSubqueryDepth: {stats.MaxSubqueryDepth}, functions: {stats.FunctionCount}, " +
            $"CTEs: {stats.CteCount}, union: {stats.HasUnion}");
    }

    private static void AppendFindings(StringBuilder builder, SqlAnalysis analysis)
    {
        var findings = analysis.Findings;
        if (findings.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Findings (address the most impactful ones first)");
        foreach (var finding in findings.Take(MaxFindingFindings))
        {
            builder.AppendLine($"- [{finding.RuleId}] ({finding.Severity.ToString().ToLowerInvariant()}, " +
                $"confidence {finding.Confidence:0.##}) {finding.Message}");
            if (!string.IsNullOrWhiteSpace(finding.SqlFragment))
            {
                builder.AppendLine($"  fragment: {finding.SqlFragment}");
            }

            foreach (var recommendation in finding.Recommendations)
            {
                builder.AppendLine($"  suggestion: {recommendation}");
            }
        }
    }

    private static void AppendPlan(StringBuilder builder, SqlOptimizationContext context)
    {
        if (context.Plan.Actions.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Proposed deterministic actions");
        foreach (var action in context.Plan.Actions)
        {
            builder.AppendLine($"- {action.ActionType} (risk {action.Risk.ToString().ToLowerInvariant()}, " +
                $"confidence {action.Confidence:0.##}): {action.Description}");
            if (!string.IsNullOrWhiteSpace(action.SqlFragment))
            {
                builder.AppendLine($"  fragment: {action.SqlFragment}");
            }
        }
    }

    private static void AppendIndexes(StringBuilder builder, SqlOptimizationContext context)
    {
        if (context.IndexRecommendations.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Index recommendations (informational; do NOT emit CREATE INDEX)");
        foreach (var index in context.IndexRecommendations)
        {
            builder.AppendLine($"- {index.Table}: ({string.Join(", ", index.KeyColumns)}) " +
                (index.IncludedColumns.Count > 0
                    ? $"INCLUDE ({string.Join(", ", index.IncludedColumns)}) "
                    : "") +
                $"confidence {index.Confidence:0.##}");
        }
    }

    private static void AppendSchema(StringBuilder builder, SqlOptimizationContext context)
    {
        if (context.Schema is { Tables.Count: > 0 })
        {
            var schemaText = BuildSchemaText(context.Schema);
            builder.AppendLine();
            builder.AppendLine("## Schema metadata");
            builder.AppendLine(schemaText.Length > MaxSchemaChars
                ? schemaText[..MaxSchemaChars] + "\n... (truncated)"
                : schemaText);
        }
    }

    private static void AppendPlanXml(StringBuilder builder, SqlOptimizationContext context)
    {
        if (string.IsNullOrWhiteSpace(context.ExecutionPlanXml))
        {
            return;
        }

        var planXml = context.ExecutionPlanXml.Trim();
        builder.AppendLine();
        builder.AppendLine("## Execution plan excerpt (may be truncated)");
        builder.AppendLine("<plan>");
        builder.AppendLine(planXml.Length > MaxPlanXmlChars ? planXml[..MaxPlanXmlChars] : planXml);
        builder.AppendLine("</plan>");
    }

    private static string BuildSchemaText(Domain.Metadata.DatabaseSchema schema)
    {
        var builder = new StringBuilder();
        foreach (var table in schema.Tables)
        {
            builder.AppendLine($"### {table.Schema}.{table.Name}" +
                (table.EstimatedRowCount is { } rows ? $" (~{rows} rows)" : ""));
            builder.AppendLine("columns:");
            foreach (var column in table.Columns)
            {
                builder.AppendLine(
                    $"- {column.Name} {column.DataType}{(column.Nullable ? " NULL" : "")}" +
                    (column.PrimaryKey ? " PRIMARY KEY" : ""));
            }

            if (table.Indexes.Count > 0)
            {
                builder.AppendLine("indexes:");
                foreach (var index in table.Indexes)
                {
                    builder.AppendLine($"- {index.Name}: ({string.Join(", ", index.KeyColumns)})" +
                        (index.IncludedColumns.Count > 0
                            ? $" INCLUDE ({string.Join(", ", index.IncludedColumns)})"
                            : "") +
                        (index.Unique ? " UNIQUE" : "") +
                        (index.Clustered ? " CLUSTERED" : ""));
                }
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }
}
