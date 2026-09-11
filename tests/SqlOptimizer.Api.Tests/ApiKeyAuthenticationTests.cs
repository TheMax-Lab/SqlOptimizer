using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SqlOptimizer.Api.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Api.Tests;

/// <summary>
/// M7 offline tests for the optional API-key authentication middleware:
/// default off (no-op), and when enabled every /api request requires the
/// configured key (constant-time compared), returning 401 otherwise. The key
/// is supplied per-factory through configuration, never through source.
/// </summary>
public sealed class ApiKeyAuthenticationTests
{
    private const string TestApiKey = "m7-test-key-not-a-real-secret";

    private static IReadOnlyDictionary<string, string> KeySettings() => new Dictionary<string, string>
    {
        ["Api__RequireApiKey"] = "true",
        ["Api__ApiKey"] = TestApiKey
    };

    [Fact]
    public async Task MissingKey_Returns401WithWwwAuthenticate()
    {
        await using var factory = ApiFactory.Create(KeySettings());
        using var client = factory.CreateDefaultClient();

        var response = await client.GetAsync("/api/v1/health");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().Contain(w => w.Scheme == "ApiKey");
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetString().Should().Be("API_KEY_REQUIRED");
    }

    [Fact]
    public async Task WrongKey_Returns401()
    {
        await using var factory = ApiFactory.Create(KeySettings());
        using var client = factory.CreateDefaultClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/health");
        request.Headers.TryAddWithoutValidation("X-Api-Key", "wrong-key");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CorrectKey_Returns200()
    {
        await using var factory = ApiFactory.Create(KeySettings());
        using var client = factory.CreateDefaultClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/health");
        request.Headers.TryAddWithoutValidation("X-Api-Key", TestApiKey);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CorrectKey_AllowsAnalyze()
    {
        await using var factory = ApiFactory.Create(KeySettings());
        using var client = factory.CreateDefaultClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/analyze");
        request.Content = JsonContent.Create(new { sql = "SELECT 1" });
        request.Headers.TryAddWithoutValidation("X-Api-Key", TestApiKey);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RejectionDoesNotLeakTheConfiguredKey()
    {
        await using var factory = ApiFactory.Create(KeySettings());
        using var client = factory.CreateDefaultClient();

        var response = await client.GetAsync("/api/v1/health");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain(TestApiKey, "the configured key must never appear in responses");
    }

    [Fact]
    public async Task KeyNotRequiredByDefault_ApiIsOpen()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateDefaultClient();

        var response = await client.GetAsync("/api/v1/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task KeyOnlyProtectsApiPrefix_LegacyHealthStaysOpen()
    {
        await using var factory = ApiFactory.Create(KeySettings());
        using var client = factory.CreateDefaultClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "only /api/* is protected; the legacy /health probe stays open");
    }

    [Fact]
    public async Task CustomHeaderName_IsRespected()
    {
        await using var factory = ApiFactory.Create(new Dictionary<string, string>
        {
            ["Api__RequireApiKey"] = "true",
            ["Api__ApiKey"] = TestApiKey,
            ["Api__ApiKeyHeaderName"] = "X-Custom-Key"
        });
        using var client = factory.CreateDefaultClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/health");
        request.Headers.TryAddWithoutValidation("X-Custom-Key", TestApiKey);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
