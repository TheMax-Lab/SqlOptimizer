using System.Net;
using System.Text;

namespace SqlOptimizer.Infrastructure.Tests.Llm;

/// <summary>
/// In-memory <see cref="HttpMessageHandler"/> for OpenAI-compatible client
/// tests: records the outgoing request (method, URI, headers, body) and
/// returns a canned response or throws a canned failure. No network is ever
/// touched and no real endpoint is contacted.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) =>
        _responder = responder ?? throw new ArgumentNullException(nameof(responder));

    /// <summary>The last request received by the fake transport.</summary>
    public HttpRequestMessage? Request { get; private set; }

    /// <summary>The serialized body of the last request received.</summary>
    public string? RequestBody { get; private set; }

    /// <summary>Number of requests received by the fake transport.</summary>
    public int RequestCount { get; private set; }

    /// <summary>Whether the handler was disposed (verifies client resource ownership).</summary>
    public bool Disposed { get; private set; }

    /// <summary>Creates a canned JSON response with the given status.</summary>
    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        Request = request;
        RequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return await _responder(request).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Disposed = true;
        }

        base.Dispose(disposing);
    }
}