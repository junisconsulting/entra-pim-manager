namespace EntraPimManager.Tests.TestSupport;

using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;

/// <summary>
/// Builds a real <see cref="GraphServiceClient"/> wired to a fake HTTP handler so
/// the SDK's own deserialization runs against JSON fixtures.
/// </summary>
public static class GraphClientTestBuilder
{
    /// <summary>Creates a <see cref="GraphServiceClient"/> backed by <paramref name="handler"/>.</summary>
    public static GraphServiceClient Build(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new GraphServiceClient(httpClient, new AnonymousAuthenticationProvider());
    }
}
