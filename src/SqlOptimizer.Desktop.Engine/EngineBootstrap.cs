using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.Services;
using SqlOptimizer.Application.Services.Validation;
using SqlOptimizer.Domain.Parsing;
using SqlOptimizer.Infrastructure;
using SqlOptimizer.Infrastructure.Parsing;
using SqlOptimizer.Rules;
using SqlOptimizer.Desktop.Engine.Support;

namespace SqlOptimizer.Desktop.Engine;

/// <summary>
/// Composes the service container of the desktop engine host. The
/// registrations mirror <c>SqlOptimizer.Api/Program.cs</c> one to one (the
/// web-specific parts — Kestrel, rate limiting, Swagger, API key — are not
/// applicable to a local process host), so the engine executes exactly the
/// same Application/Rules/Infrastructure pipeline the HTTP API uses. The
/// existing projects are referenced, never modified.
/// </summary>
public static class EngineBootstrap
{
    /// <summary>Builds the service provider for the given configuration.</summary>
    /// <param name="configuration">The engine configuration.</param>
    public static ServiceProvider Build(EngineConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var services = new ServiceCollection();

        // Console/stderr logger for the infrastructure services that require
        // ILogger<T> (the ASP.NET host normally supplies these).
        services.AddSingleton(typeof(ILogger<>), typeof(StderrLogger<>));

        // Core services: parser, prompt generator, index advisor, LLM response parser.
        services.AddScoped<ISqlParser, SqlServerSqlParser>();
        services.AddScoped<IPromptGenerator, PromptGenerator>();
        services.AddScoped<IIndexAdvisor, IndexAdvisor>();
        services.AddTransient<LlmResponseParser>();

        // Deterministic analysis pipeline: 20 rules + rule registry + score engine.
        services.AddSqlOptimizerRules();

        // Global pipeline options and the analyzer use case.
        services.AddSingleton(configuration.Pipeline);
        services.AddScoped<ISqlAnalyzer, SqlAnalyzer>();

        // Database validation configuration (optional). Without a configured
        // database the safe unavailable provider is registered and the
        // pipeline runs fully in static mode.
        services.AddSingleton(configuration.Database);
        services.AddScoped<ISqlValidator, SqlValidator>();
        services.AddInfrastructure(configuration.Database);

        // LLM provider configuration (optional). Exactly one ILlmClient is
        // registered; without configuration the inert UnconfiguredLlmClient
        // keeps the deterministic path fully functional.
        services.AddSingleton(configuration.Llm);
        services.AddLlm(configuration.Llm);

        // Candidate generation and optimization orchestration.
        services.AddScoped<IOptimizationPlanBuilder, OptimizationPlanBuilder>();
        services.AddScoped<ISqlOptimizationCandidateGenerator, DeterministicCandidateGenerator>();
        services.AddScoped<ISqlOptimizationCandidateGenerator, LlmCandidateGenerator>();
        services.AddTransient<CandidateRanker>();
        services.AddScoped<ISqlOptimizer, SqlOptimizationService>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });
    }
}
