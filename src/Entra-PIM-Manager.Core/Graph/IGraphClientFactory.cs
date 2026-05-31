namespace EntraPimManager.Core.Graph;

using EntraPimManager.Core.Auth;
using Microsoft.Graph;

/// <summary>
/// Creates configured <see cref="GraphServiceClient"/> instances. All Graph access
/// in Entra PIM Manager goes through a client built here — never a raw <c>HttpClient</c>.
/// </summary>
public interface IGraphClientFactory
{
    /// <summary>
    /// Builds (or returns a cached) <see cref="GraphServiceClient"/> pinned to
    /// <paramref name="account"/>. Subsequent calls with the same account return
    /// the same client so HTTP connection pooling and Kiota middleware state are
    /// preserved.
    /// </summary>
    GraphServiceClient CreateFor(SignedInAccount account);
}
