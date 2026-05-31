namespace EntraPimManager.Tests.TestSupport;

using System.Net;
using System.Text;

/// <summary>
/// Test HTTP handler that replays a fixed sequence of responses and records the
/// requests (and their bodies) it received. The last response repeats once the
/// queue is exhausted.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;

    public FakeHttpMessageHandler(params HttpResponseMessage[] responses)
    {
        if (responses.Length == 0)
        {
            throw new ArgumentException("At least one response is required.", nameof(responses));
        }

        _responses = new Queue<HttpResponseMessage>(responses);
    }

    /// <summary>Requests received by this handler, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Request body text for each received request (<c>null</c> when there was no body).</summary>
    public List<string?> RequestBodies { get; } = [];

    /// <summary>Number of requests received.</summary>
    public int RequestCount => Requests.Count;

    /// <summary>Builds a JSON response with the given body and status.</summary>
    public static HttpResponseMessage JsonResponse(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        return _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
    }
}
