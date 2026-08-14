namespace EntraPimManager.Tests.TestSupport;

using System.Net;

/// <summary>
/// Test HTTP handler that throws a per-request exception chosen by a selector,
/// and answers a fixed status code for requests without one.
/// <see cref="FakeHttpMessageHandler"/> can only replay responses, not throw.
/// </summary>
public sealed class ThrowingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Exception?> _exceptionSelector;
    private readonly HttpStatusCode _fallbackStatus;

    public ThrowingHttpMessageHandler(
        Func<HttpRequestMessage, Exception?> exceptionSelector,
        HttpStatusCode fallbackStatus = HttpStatusCode.OK)
    {
        _exceptionSelector = exceptionSelector;
        _fallbackStatus = fallbackStatus;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var exception = _exceptionSelector(request);
        return exception is null
            ? Task.FromResult(new HttpResponseMessage(_fallbackStatus))
            : Task.FromException<HttpResponseMessage>(exception);
    }
}
