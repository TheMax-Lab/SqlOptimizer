using Microsoft.Extensions.DependencyInjection;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Infrastructure.Llm;

namespace SqlOptimizer.Infrastructure;

/// <summary>
/// Registers the optional LLM capability for dependency injection. The LLM
/// is never mandatory: when no provider is configured (or the
/// configuration is incomplete) an <see cref="UnconfiguredLlmClient"/> is
/// registered, so the application starts and the deterministic optimizer
/// runs fully without an API key, an endpoint or network access. Exactly
/// one <see cref="ILlmClient"/> implementation is always registered,
/// selected explicitly and case-insensitively by <c>Llm:Provider</c>:
/// <list type="bullet">
/// <item>"OpenAI" (with <c>Endpoint</c> and <c>ApiKey</c>) → <see cref="OpenAiCompatibleLlmClient"/></item>
/// <item>"Mock" → <see cref="MockLlmClient"/></item>
/// <item>anything else → <see cref="UnconfiguredLlmClient"/></item>
/// </list>
/// Clients are scoped (the mock holds per-scope request capture state) and
/// depend only on singleton options, so no lifetime violation is possible.
/// </summary>
public static class SqlOptimizerLlmServiceCollectionExtensions
{
    /// <summary>Registers the configured <see cref="ILlmClient"/> implementation.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The LLM options (already bound from configuration; register the same instance as a singleton if not done yet).</param>
    public static IServiceCollection AddLlm(this IServiceCollection services, LlmOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        if (string.Equals(options.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(options.Endpoint)
            && !string.IsNullOrWhiteSpace(options.ApiKey))
        {
            services.AddScoped<ILlmClient, OpenAiCompatibleLlmClient>();
        }
        else if (string.Equals(options.Provider, "Mock", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<ILlmClient, MockLlmClient>();
        }
        else
        {
            services.AddScoped<ILlmClient, UnconfiguredLlmClient>();
        }

        return services;
    }
}