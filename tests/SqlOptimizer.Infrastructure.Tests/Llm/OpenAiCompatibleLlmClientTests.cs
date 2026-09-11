using System.Net;
using System.Text.Json;
using FluentAssertions;
using SqlOptimizer.Application.Abstractions;
using SqlOptimizer.Application.Options;
using SqlOptimizer.Domain.Common;
using SqlOptimizer.Infrastructure.Llm;
using Xunit;

namespace SqlOptimizer.Infrastructure.Tests.Llm;

/// <summary>
/// OpenAiCompatibleLlmClient verified against an in-memory
/// <see cref="HttpMessageHandler"/>: HTTP method, endpoint composition,
/// authentication, request serialization, response parsing, controlled
/// errors, cancellation, timeout and secret non-leakage. No real LLM
/// service is contacted and no network access is required.
/// </summary>
public class OpenAiCompatibleLlmClientTests
{
    private const string ApiKey = "sk-test-fake-key-123";
    private const string Endpoint = "https://llm.example.test/v1";

    private const string SuccessBody = """
        {
          "id": "chatcmpl-1",
          "model": "test-model",
          "choices": [
            { "index": 0, "message": { "role": "assistant", "content": "hello" }, "finish_reason": "stop" }
          ],
          "usage": { "prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15 }
        }
        """;

    private static LlmOptions Options(string? endpoint = null, string? apiKey = null, int timeoutSeconds = 30, string? model = null) =>
        new(Provider: "OpenAI", Model: model ?? "test-model", ApiKey: apiKey ?? ApiKey, Endpoint: endpoint ?? Endpoint, TimeoutSeconds: timeoutSeconds);

    private static OpenAiCompatibleLlmClient CreateClient(FakeHttpMessageHandler handler, LlmOptions? options = null) =>
        new(options ?? Options(), handler);

    private static string RequestModel(FakeHttpMessageHandler handler) =>
        JsonDocument.Parse(handler.RequestBody!).RootElement.GetProperty("model").GetString()!;

    [Fact]
    public async Task SendsPostRequest_ToChatCompletionsEndpoint()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody)));
        using var client = CreateClient(handler);

        await client.CompleteAsync(new LlmRequest("sys", "user"));

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.ToString().Should().Be("https://llm.example.test/v1/chat/completions");
    }

    [Theory]
    [InlineData("https://example.com/v1", "https://example.com/v1/chat/completions")]
    [InlineData("https://example.com/v1/", "https://example.com/v1/chat/completions")]
    [InlineData("https://example.com", "https://example.com/chat/completions")]
    [InlineData("https://example.com/", "https://example.com/chat/completions")]
    public async Task BaseUrlVariants_ProduceWellFormedEndpoint(string baseUrl, string expected)
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody)));
        using var client = CreateClient(handler, Options(endpoint: baseUrl));

        await client.CompleteAsync(new LlmRequest("sys", "user"));

        handler.Request!.RequestUri!.ToString().Should().Be(expected);
    }

    [Fact]
    public async Task SendsBearerAuthorizationHeader_AndJsonContentType()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody)));
        using var client = CreateClient(handler);

        await client.CompleteAsync(new LlmRequest("sys", "user"));

        var header = handler.Request!.Headers.Authorization;
        header.Should().NotBeNull();
        header!.Scheme.Should().Be("Bearer");
        header.Parameter.Should().Be(ApiKey);
        handler.Request.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task SerializesRequestJson_WithPromptsTemperatureAndMaxTokens()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody)));
        using var client = CreateClient(handler);

        await client.CompleteAsync(new LlmRequest("system prompt text", "user prompt text", Temperature: 0.5, MaxTokens: 777));

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        root.GetProperty("model").GetString().Should().Be("test-model");
        root.GetProperty("temperature").GetDouble().Should().Be(0.5);
        root.GetProperty("max_tokens").GetInt32().Should().Be(777);
        var messages = root.GetProperty("messages").EnumerateArray().ToArray();
        messages.Should().HaveCount(2);
        messages[0].GetProperty("role").GetString().Should().Be("system");
        messages[0].GetProperty("content").GetString().Should().Be("system prompt text");
        messages[1].GetProperty("role").GetString().Should().Be("user");
        messages[1].GetProperty("content").GetString().Should().Be("user prompt text");
    }

    [Fact]
    public async Task OptionsDefaults_AreUsed_WhenRequestOmitsGenerationSettings()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody)));
        using var client = CreateClient(handler);

        await client.CompleteAsync(new LlmRequest("sys", "user"));

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        root.GetProperty("temperature").GetDouble().Should().Be(0.1);
        root.GetProperty("max_tokens").GetInt32().Should().Be(2_000);
    }

    [Fact]
    public async Task Model_UsesOptionsValue_WhenRequestHasNone()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody)));
        using var client = CreateClient(handler);

        await client.CompleteAsync(new LlmRequest("sys", "user"));

        RequestModel(handler).Should().Be("test-model");
    }

    [Fact]
    public async Task Model_RequestValue_OverridesOptionsValue()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody)));
        using var client = CreateClient(handler);

        await client.CompleteAsync(new LlmRequest("sys", "user", Model: "request-model"));

        RequestModel(handler).Should().Be("request-model");
    }

    [Fact]
    public async Task Model_FallsBackToDefault_WhenBothAreEmpty()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody)));
        using var client = CreateClient(handler, Options(model: ""));

        await client.CompleteAsync(new LlmRequest("sys", "user"));

        RequestModel(handler).Should().Be("gpt-4o-mini");
    }

    [Fact]
    public async Task ApiKey_IsNotIncludedInRequestBody()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody)));
        using var client = CreateClient(handler);

        await client.CompleteAsync(new LlmRequest("sys", "user"));

        handler.RequestBody.Should().NotContain(ApiKey);
    }

    [Fact]
    public async Task SuccessfulResponse_ParsesContentModelUsageAndFinishReason()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody)));
        using var client = CreateClient(handler);

        var response = await client.CompleteAsync(new LlmRequest("sys", "user"));

        response.Content.Should().Be("hello");
        response.Model.Should().Be("test-model");
        response.PromptTokens.Should().Be(10);
        response.CompletionTokens.Should().Be(5);
        response.FinishReason.Should().Be("stop");
        response.DurationSeconds.Should().BeGreaterThan(0);
        handler.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task OptionalFieldsOmitted_AreNull()
    {
        const string body = """{"choices":[{"message":{"content":"ok"}}]}""";
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, body)));
        using var client = CreateClient(handler);

        var response = await client.CompleteAsync(new LlmRequest("sys", "user"));

        response.Content.Should().Be("ok");
        response.Model.Should().BeNull();
        response.PromptTokens.Should().BeNull();
        response.CompletionTokens.Should().BeNull();
        response.FinishReason.Should().BeNull();
    }

    [Fact]
    public async Task EmptyContent_ReturnsEmptyResponse()
    {
        const string body = """{"choices":[{"message":{"content":""},"finish_reason":"stop"}]}""";
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, body)));
        using var client = CreateClient(handler);

        var response = await client.CompleteAsync(new LlmRequest("sys", "user"));

        response.Content.Should().BeEmpty();
    }

    [Fact]
    public async Task MalformedJson_ThrowsLlmException()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, "this is not json")));
        using var client = CreateClient(handler);

        var act = () => client.CompleteAsync(new LlmRequest("sys", "user"));

        var exception = (await act.Should().ThrowAsync<LlmException>()).Which;
        exception.Message.Should().Contain("unexpected response");
        exception.Message.Should().NotContain(ApiKey);
    }

    [Fact]
    public async Task MissingChoices_ThrowsLlmException()
    {
        const string body = """{"id":"chatcmpl-1","model":"test-model"}""";
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, body)));
        using var client = CreateClient(handler);

        var act = () => client.CompleteAsync(new LlmRequest("sys", "user"));

        (await act.Should().ThrowAsync<LlmException>()).Which.Message.Should().Contain("unexpected response");
    }

    [Fact]
    public async Task MissingContentField_ThrowsLlmException()
    {
        const string body = """{"choices":[{"message":{"role":"assistant"},"finish_reason":"stop"}]}""";
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, body)));
        using var client = CreateClient(handler);

        var act = () => client.CompleteAsync(new LlmRequest("sys", "user"));

        (await act.Should().ThrowAsync<LlmException>()).Which.Message.Should().Contain("unexpected response");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ProviderHttpErrors_ThrowLlmException_WithStatusCode(HttpStatusCode status)
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(status, """{"error":"nope"}""")));
        using var client = CreateClient(handler);

        var act = () => client.CompleteAsync(new LlmRequest("sys", "user"));

        var exception = (await act.Should().ThrowAsync<LlmException>()).Which;
        exception.Message.Should().Contain($"HTTP {(int)status}");
        exception.Message.Should().NotContain(ApiKey);
    }

    [Fact]
    public async Task ProviderErrorBody_ContainingApiKey_IsRedacted()
    {
        var body = "{\"error\":\"Incorrect API key provided: " + ApiKey + "\"}";
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, body)));
        using var client = CreateClient(handler);

        var act = () => client.CompleteAsync(new LlmRequest("sys", "user"));

        var exception = (await act.Should().ThrowAsync<LlmException>()).Which;
        exception.Message.Should().Contain("[REDACTED]");
        exception.Message.Should().NotContain(ApiKey);
    }

    [Fact]
    public async Task NetworkFailure_ThrowsSanitizedLlmException()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException($"connection to {Endpoint} failed"));
        using var client = CreateClient(handler);

        var act = () => client.CompleteAsync(new LlmRequest("sys", "user"));

        var exception = (await act.Should().ThrowAsync<LlmException>()).Which;
        exception.Message.Should().Be("The LLM request failed: the provider could not be reached.");
        exception.Message.Should().NotContain("llm.example.test");
        exception.Message.Should().NotContain(ApiKey);
        exception.InnerException.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task PreCancelledToken_ThrowsOperationCanceledException()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody)));
        using var client = CreateClient(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => client.CompleteAsync(new LlmRequest("sys", "user"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SlowProvider_ThrowsLlmException_OnTimeout()
    {
        var handler = new FakeHttpMessageHandler(async _ =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(2000));
            return FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody);
        });
        using var client = CreateClient(handler, Options(timeoutSeconds: 1));

        var act = () => client.CompleteAsync(new LlmRequest("sys", "user"));

        var exception = (await act.Should().ThrowAsync<LlmException>()).Which;
        exception.Message.Should().Contain("timed out after 1 seconds");
        exception.Message.Should().NotContain(ApiKey);
    }

    [Fact]
    public async Task MissingEndpoint_ThrowsLlmNotConfigured_NoRequestSent()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody)));
        using var client = CreateClient(handler, Options(endpoint: ""));

        var act = () => client.CompleteAsync(new LlmRequest("sys", "user"));

        (await act.Should().ThrowAsync<LlmNotConfiguredException>()).Which.Message.Should().Contain("Llm:Endpoint");
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task MissingApiKey_ThrowsLlmNotConfigured_NoRequestSent()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody)));
        using var client = CreateClient(handler, Options(apiKey: ""));

        var act = () => client.CompleteAsync(new LlmRequest("sys", "user"));

        (await act.Should().ThrowAsync<LlmNotConfiguredException>()).Which.Message.Should().Contain("Llm:ApiKey");
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task InvalidEndpoint_ThrowsLlmNotConfigured_NoRequestSent()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody)));
        using var client = CreateClient(handler, Options(endpoint: "not a valid url"));

        var act = () => client.CompleteAsync(new LlmRequest("sys", "user"));

        (await act.Should().ThrowAsync<LlmNotConfiguredException>()).Which.Message.Should().Contain("valid absolute");
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public void NullOptions_ThrowsArgumentNullException()
    {
        var act = () => new OpenAiCompatibleLlmClient(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task NullRequest_ThrowsArgumentNullException()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody)));
        using var client = CreateClient(handler);

        var act = () => client.CompleteAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public void DisposingClient_DisposesOwnedHandler()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody)));
        var client = new OpenAiCompatibleLlmClient(Options(), handler);

        client.Dispose();

        handler.Disposed.Should().BeTrue();
    }

    [Fact]
    public void DisposingSharedTransportClient_DoesNotThrow()
    {
        var client = new OpenAiCompatibleLlmClient(Options());

        var act = () => client.Dispose();

        act.Should().NotThrow();
    }
}