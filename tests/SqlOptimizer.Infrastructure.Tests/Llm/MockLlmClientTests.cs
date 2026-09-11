using FluentAssertions;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Infrastructure.Llm;
using Xunit;

namespace SqlOptimizer.Infrastructure.Tests.Llm;

/// <summary>
/// MockLlmClient: deterministic, offline, configurable. Verifies the fixed
/// response contract, request capture, cancellation and the absence of any
/// network or random behavior.
/// </summary>
public class MockLlmClientTests
{
    [Fact]
    public async Task Default_ReturnsParseableEmptyCandidateList()
    {
        var client = new MockLlmClient();

        var response = await client.CompleteAsync(new LlmRequest("system", "user"));

        response.Content.Should().Be("{\"candidates\":[]}");
        response.Model.Should().Be("mock-1");
        response.FinishReason.Should().Be("stop");
        response.DurationSeconds.Should().Be(0);
    }

    [Fact]
    public async Task ConfiguredResponse_IsReturnedForEveryRequest()
    {
        const string content = "{\"candidates\":[{\"sql\":\"SELECT 1\"}]}";
        var client = new MockLlmClient(content);

        var first = await client.CompleteAsync(new LlmRequest("s1", "u1"));
        var second = await client.CompleteAsync(new LlmRequest("s2", "u2"));

        first.Content.Should().Be(content);
        second.Content.Should().Be(content);
    }

    [Fact]
    public async Task Deterministic_RepeatedCallsReturnEqualResponses()
    {
        var client = new MockLlmClient();

        var first = await client.CompleteAsync(new LlmRequest("s", "u"));
        var second = await client.CompleteAsync(new LlmRequest("s", "u"));

        first.Should().BeEquivalentTo(second);
        client.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Requests_AreCapturedInOrder_WithFullContent()
    {
        var client = new MockLlmClient();

        await client.CompleteAsync(new LlmRequest("system-1", "user-1", Temperature: 0.2, MaxTokens: 100, Model: "model-a"));
        await client.CompleteAsync(new LlmRequest("system-2", "user-2"));

        client.CallCount.Should().Be(2);
        var requests = client.Requests;
        requests.Should().HaveCount(2);
        requests[0].SystemPrompt.Should().Be("system-1");
        requests[0].UserPrompt.Should().Be("user-1");
        requests[0].Temperature.Should().Be(0.2);
        requests[0].MaxTokens.Should().Be(100);
        requests[0].Model.Should().Be("model-a");
        requests[1].SystemPrompt.Should().Be("system-2");
        requests[1].UserPrompt.Should().Be("user-2");
        requests[1].Temperature.Should().BeNull();
        requests[1].Model.Should().BeNull();
    }

    [Fact]
    public async Task PreCancelledToken_ThrowsOperationCanceled_AndDoesNotCapture()
    {
        var client = new MockLlmClient();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => client.CompleteAsync(new LlmRequest("s", "u"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        client.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task NullRequest_IsRejected()
    {
        var client = new MockLlmClient();

        var act = () => client.CompleteAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void NullResponseContent_IsRejected()
    {
        var act = () => new MockLlmClient(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}