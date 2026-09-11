using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using SqlOptimizer.Application.DTOs;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SqlOptimizer.Api.OpenApi;

/// <summary>
/// Attaches concrete JSON examples to the OpenAPI schemas of the public API
/// request/response DTOs (Swashbuckle 6.x has no dedicated examples
/// providers, so examples are applied through the schema's Example property).
/// Examples are static, safe documentation values; they never influence
/// runtime behavior.
/// </summary>
public sealed class ApiExamplesSchemaFilter : ISchemaFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(SqlAnalysisRequest))
        {
            schema.Example = new OpenApiObject
            {
                ["sql"] = new OpenApiString("SELECT c.Id, c.Name, o.Total FROM dbo.Customers AS c JOIN dbo.Orders AS o ON o.CustomerId = c.Id WHERE c.City = N'Rome' ORDER BY c.Id"),
                ["dialect"] = new OpenApiString("SqlServer"),
                ["includeAst"] = new OpenApiBoolean(false)
            };
        }
        else if (context.Type == typeof(SqlOptimizationRequest))
        {
            schema.Example = new OpenApiObject
            {
                ["sql"] = new OpenApiString("SELECT c.Id, c.Name, c.City FROM dbo.Customers AS c WHERE c.City = N'Rome' ORDER BY c.Id"),
                ["dialect"] = new OpenApiString("SqlServer"),
                ["options"] = new OpenApiObject
                {
                    ["useLlm"] = new OpenApiBoolean(false),
                    ["generateIndexes"] = new OpenApiBoolean(true),
                    ["validateSemantics"] = new OpenApiBoolean(true),
                    ["generatePrompt"] = new OpenApiBoolean(false),
                    ["maxCandidates"] = new OpenApiInteger(3),
                    ["strategy"] = new OpenApiString("Balanced")
                }
            };
        }
        else if (context.Type == typeof(SqlValidationRequest))
        {
            schema.Example = new OpenApiObject
            {
                ["originalSql"] = new OpenApiString("SELECT * FROM dbo.Customers ORDER BY Id"),
                ["candidateSql"] = new OpenApiString("SELECT Id, Name, City FROM dbo.Customers ORDER BY Id"),
                ["compareResults"] = new OpenApiBoolean(true),
                ["maxRowsForComparison"] = new OpenApiInteger(1000),
                ["dialect"] = new OpenApiString("SqlServer")
            };
        }
        else if (context.Type == typeof(ValidationResult))
        {
            schema.Example = new OpenApiObject
            {
                ["status"] = new OpenApiString("Inconclusive"),
                ["syntaxValid"] = new OpenApiBoolean(true),
                ["semanticallyEquivalent"] = new OpenApiBoolean(false),
                ["validationConfidence"] = new OpenApiDouble(0.4),
                ["limitations"] = new OpenApiArray
                {
                    new OpenApiString("Runtime result comparison was requested but no database validation provider is available.")
                }
            };
        }
    }
}
