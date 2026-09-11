using SqlOptimizer.Application.Abstractions;

namespace SqlOptimizer.Infrastructure.Llm;

/// <summary>
/// Deterministic offline LLM stand-in (<c>Llm:Provider = "Mock"</c>). It
/// never calls any network endpoint, contains no secrets and produces no
/// random behavior. By default it returns a fixed, parseable response — an
/// empty candidate list — so the pipeline degrades to the deterministic
/// generator without fabricating LLM output. Tests may configure a
/// predictable fixed response and inspect every request the client
/// received (captured in order).
/// </summary>
public sealed class MockLlmClient : ILlmClient
{
    /// <summary>Default fixed response: a parseable empty candidate list.</summary>
    public const string DefaultResponseContent = "{\"candidates\":[]}";

    /// <summary>Model identifier reported by the mock.</summary>
    public const string DefaultModel = "mock-1";

    private readonly LlmResponse _response;
    private readonly List<LlmRequest> _requests = [];

    /// <summary>Creates a mock that always returns <see cref="DefaultResponseContent"/>.</summary>
    public MockLlmClient()
        : this(DefaultResponseContent)
    {
    }

    /// <summary>Creates a mock that always returns the given fixed content.</summary>
    /// <param name="responseContent">The fixed completion content returned for every request.</param>
    /// <param name="model">The model identifier reported in the response (default <c>mock-1</c>).</param>
    public MockLlmClient(string responseContent, string? model = DefaultModel)
    {
        ArgumentNullException.ThrowIfNull(responseContent);

        _response = new LlmResponse(
            responseContent,
            Model: model,
            PromptTokens: null,
            CompletionTokens: null,
            DurationSeconds: 0,
            FinishReason: "stop");
    }

    /// <summary>Requests received so far, in order (snapshot copy).</summary>
    public IReadOnlyList<LlmRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return new List<LlmRequest>(_requests);
            }
        }
    }

    /// <summary>Number of requests received so far.</summary>
    public int CallCount
    {
        get
        {
            lock (_requests)
            {
                return _requests.Count;
            }
        }
    }

    /// <inheritdoc />
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_requests)
        {
            _requests.Add(request);
        }

        return Task.FromResult(_response);
    }
}
