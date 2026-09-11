using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SqlOptimizer.Api.Tests.TestSupport;
using Xunit;

namespace SqlOptimizer.Api.Tests;

/// <summary>
/// M7 offline tests for the optional per-client rate limiting: the token
/// bucket budget is configurable, rejections are 429 with Retry-After and a
/// machine-readable code, and the limiter can be disabled entirely.
/// </summary>
public sealed class RateLimitingTests
{
    [Fact]
    public async Task ExceedingBudget_Returns429WithRetryAfter()
    {
        await using var factory = ApiFactory.Create(new Dictionary<string, string>
        {
            ["Api__RateLimitPerMinute"] = "2"
        });
        using var client = factory.CreateDefaultClient();

        var first = await client.PostAsJsonAsync("/api/v1/analyze", new { sql = "SELECT 1" });
        var second = await client.PostAsJsonAsync("/api/v1/analyze", new { sql = "SELECT 1" });
        var third = await client.PostAsJsonAsync("/api/v1/analyze", new { sql = "SELECT 1" });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        third.Headers.RetryAfter.Should().NotBeNull("throttled responses must carry Retry-After");
        third.Headers.RetryAfter!.Delta.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));

        using var doc = JsonDocument.Parse(await third.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("RATE_LIMITED");
        doc.RootElement.GetProperty("requestId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Disabled_RateLimitingAllowsBursts()
    {
        await using var factory = ApiFactory.Create(new Dictionary<string, string>
        {
            ["Api__EnableRateLimiting"] = "false",
            ["Api__RateLimitPerMinute"] = "1"
        });
        using var client = factory.CreateDefaultClient();

        HttpStatusCode[] codes =
        [
            (await client.PostAsJsonAsync("/api/v1/analyze", new { sql = "SELECT 1" })).StatusCode,
            (await client.PostAsJsonAsync("/api/v1/analyze", new { sql = "SELECT 1" })).StatusCode,
            (await client.PostAsJsonAsync("/api/v1/analyze", new { sql = "SELECT 1" })).StatusCode,
            (await client.PostAsJsonAsync("/api/v1/analyze", new { sql = "SELECT 1" })).StatusCode,
            (await client.PostAsJsonAsync("/api/v1/analyze", new { sql = "SELECT 1" })).StatusCode
        ];

        codes.Should().OnlyContain(c => c == HttpStatusCode.OK, "with rate limiting disabled no 429 must be produced");
    }
}
