using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Infrastructure;
using SqlOptimizer.Infrastructure.Llm;
using Xunit;

namespace SqlOptimizer.Infrastructure.Tests.Di;

/// <summary>
/// AddLlm registration contracts: explicit configuration-driven selection,
/// exactly one ILlmClient in every mode, scoped lifetime, and the safe
/// disabled default (no API key, endpoint or network required at startup).
/// </summary>
public class LlmServiceCollectionTests
{
    private static ServiceCollection BuildServices(LlmOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddLlm(options);
        return services;
    }

    private static ServiceProvider BuildProvider(LlmOptions options) =>
        BuildServices(options).BuildServiceProvider();

    [Fact]
    public void OpenAiProviderWithEndpointAndKey_RegistersOpenAiClient()
    {
        using var provider = BuildProvider(new LlmOptions(Provider: "OpenAI", Endpoint: "https://llm.example.test/v1", ApiKey: "key"));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ILlmClient>().Should().BeOfType<OpenAiCompatibleLlmClient>();
    }

    [Fact]
    public void ProviderMatching_IsCaseInsensitive()
    {
        using var provider = BuildProvider(new LlmOptions(Provider: "openai", Endpoint: "https://llm.example.test/v1", ApiKey: "key"));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ILlmClient>().Should().BeOfType<OpenAiCompatibleLlmClient>();
    }

    [Fact]
    public void OpenAiProviderMissingApiKey_FallsBackToUnconfiguredClient()
    {
        using var provider = BuildProvider(new LlmOptions(Provider: "OpenAI", Endpoint: "https://llm.example.test/v1", ApiKey: ""));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ILlmClient>().Should().BeOfType<UnconfiguredLlmClient>();
    }

    [Fact]
    public void OpenAiProviderMissingEndpoint_FallsBackToUnconfiguredClient()
    {
        using var provider = BuildProvider(new LlmOptions(Provider: "OpenAI", Endpoint: "", ApiKey: "key"));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ILlmClient>().Should().BeOfType<UnconfiguredLlmClient>();
    }

    [Fact]
    public void MockProvider_RegistersMockClient_WithoutCredentials()
    {
        using var provider = BuildProvider(new LlmOptions(Provider: "Mock"));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ILlmClient>().Should().BeOfType<MockLlmClient>();
    }

    [Fact]
    public void EmptyProvider_RegistersUnconfiguredClient()
    {
        using var provider = BuildProvider(new LlmOptions());
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ILlmClient>().Should().BeOfType<UnconfiguredLlmClient>();
    }

    [Fact]
    public void UnknownProvider_RegistersUnconfiguredClient()
    {
        using var provider = BuildProvider(new LlmOptions(Provider: "Azure"));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ILlmClient>().Should().BeOfType<UnconfiguredLlmClient>();
    }

    [Fact]
    public void ExactlyOneLlmClient_IsRegistered_InEveryMode()
    {
        var modes = new[]
        {
            new LlmOptions(),
            new LlmOptions(Provider: "Mock"),
            new LlmOptions(Provider: "OpenAI", Endpoint: "https://llm.example.test/v1", ApiKey: "key"),
            new LlmOptions(Provider: "OpenAI", Endpoint: "", ApiKey: "key"),
            new LlmOptions(Provider: "Azure"),
        };

        foreach (var options in modes)
        {
            BuildServices(options).Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(ILlmClient));
        }
    }

    [Fact]
    public void LlmClient_IsScoped()
    {
        using var provider = BuildProvider(new LlmOptions(Provider: "OpenAI", Endpoint: "https://llm.example.test/v1", ApiKey: "key"));
        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        // Same scope: the same instance (scoped lifetime).
        scopeA.ServiceProvider.GetRequiredService<ILlmClient>()
            .Should().BeSameAs(scopeA.ServiceProvider.GetRequiredService<ILlmClient>());

        // Different scopes: different instances.
        scopeA.ServiceProvider.GetRequiredService<ILlmClient>()
            .Should().NotBeSameAs(scopeB.ServiceProvider.GetRequiredService<ILlmClient>());
    }

    [Fact]
    public void DisabledMode_StartupSucceeds_WithoutCredentials()
    {
        // Default options: no provider, no key, no endpoint — the equivalent
        // of a deployment where the LLM section is absent.
        using var provider = BuildProvider(new LlmOptions());
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ILlmClient>().Should().BeOfType<UnconfiguredLlmClient>();
    }

    [Fact]
    public async Task MockMode_ResolvedClientIsUsable_Offline()
    {
        using var provider = BuildProvider(new LlmOptions(Provider: "Mock"));
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ILlmClient>();

        var response = await client.CompleteAsync(new LlmRequest("s", "u"));

        response.Content.Should().Be(MockLlmClient.DefaultResponseContent);
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        var services = new ServiceCollection();

        Action a1 = () => services.AddLlm(null!);
        a1.Should().Throw<ArgumentNullException>();

        Action a2 = () => ((IServiceCollection)null!).AddLlm(new LlmOptions());
        a2.Should().Throw<ArgumentNullException>();
    }
}