using SqlOptimizer.Api.Health;
using SqlOptimizer.Api.Options;
using SqlOptimizer.Application.DTOs;

namespace SqlOptimizer.Api.Endpoints;

/// <summary>
/// Route mapping for the public versioned API. Called once from Program.cs
/// as <c>app.MapV1Api()</c>. Endpoints delegate to the thin static handlers
/// in <see cref="ApiEndpoints"/>. Request bodies are capped server-wide via
/// Kestrel (<see cref="ApiOptions.MaxRequestBodyBytes"/>, see Program.cs).
/// </summary>
public static class ApiEndpointExtensions
{
    /// <summary>Maps all /api/v1 endpoints.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    public static IEndpointRouteBuilder MapV1Api(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1").WithTags("SqlOptimizer v1");

        group.MapPost("/analyze", ApiEndpoints.AnalyzeAsync)
            .WithSummary("Analyze a SQL query")
            .WithDescription("Runs the deterministic analysis pipeline (parse, rules, scores) on a T-SQL query and returns findings, deterministic scores and structural statistics. Input errors map to 400 (SQL_INVALID_INPUT, SQL_PARSE_ERROR, SQL_UNSUPPORTED_DIALECT, SQL_TOO_LONG); oversized bodies to 413.")
            .Produces<AnalysisResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost("/optimize", ApiEndpoints.OptimizeAsync)
            .WithSummary("Optimize a SQL query")
            .WithDescription("Runs the full optimization pipeline (analyze, deterministic plan, candidate generation, mandatory validation, deterministic ranking). Every candidate carries its own validation result; Inconclusive is never reinterpreted as success. Input errors map to 400; oversized bodies to 413.")
            .Produces<SqlOptimizationResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost("/validate", ApiEndpoints.ValidateAsync)
            .WithSummary("Validate a candidate query")
            .WithDescription("Validates a candidate SQL query against the original (syntax, structure, semantic risk, optional runtime result comparison when a database is configured). The domain ValidationResult is returned as-is with 200: Passed, Failed and Inconclusive are all 200 outcomes; Inconclusive is never a success and no database result is ever fabricated. Original-SQL input errors map to 400.")
            .Produces<ValidationResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet("/health", ApiEndpoints.HealthAsync)
            .WithSummary("API health (liveness + database readiness)")
            .WithDescription("Machine-readable health: application liveness, whether a database is configured and whether it answered a fast SELECT 1 ping. Never returns connection strings or credentials; performs no heavy operations.")
            .Produces<ApiHealthResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }
}
