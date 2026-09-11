using FluentAssertions;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Infrastructure.Llm;
using Xunit;

namespace SqlOptimizer.Infrastructure.Tests.Llm;

/// <summary>
/// UnconfiguredLlmClient: fail-closed behavior when no provider is
/// configured. Every call must fail with a controlled, descriptive
/// exception — never a fabricated response.
/// </summary>
public class UnconfiguredLlmClientTests
{
    [Fact]
    public async Task CompleteAsync_ThrowsLlmNotConfiguredException()
    {
        var client = new UnconfiguredLlmClient();

        var act = () => client.CompleteAsync(new LlmRequest("s", "u"));

        var exception = (await act.Should().ThrowAsync<LlmNotConfiguredException>()).Which;
        exception.Message.Should().Contain("Llm:Provider");
    }

    [Fact]
    public async Task Failure_IsDeterministic_AcrossRepeatedCalls()
    {
        var client = new UnconfiguredLlmClient();

        var act1 = () => client.CompleteAsync(new LlmRequest("s", "u1"));
        var act2 = () => client.CompleteAsync(new LlmRequest("s", "u2"));

        var exception1 = (await act1.Should().ThrowAsync<LlmNotConfiguredException>()).Which;
        var exception2 = (await act2.Should().ThrowAsync<LlmNotConfiguredException>()).Which;
        exception1.Message.Should().Be(exception2.Message);
    }
}