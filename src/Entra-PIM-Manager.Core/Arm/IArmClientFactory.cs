namespace EntraPimManager.Core.Arm;

using EntraPimManager.Core.Auth;

/// <summary>
/// Creates configured Azure Resource Manager clients. All ARM access in Entra
/// PIM Manager goes through a client built here — never a raw <c>HttpClient</c>
/// — so auth, retry and the claims-challenge handler apply to every call.
/// </summary>
public interface IArmClientFactory
{
    /// <summary>
    /// Builds (or returns a cached) ARM <see cref="HttpClient"/> pinned to
    /// <paramref name="account"/>, with the cloud's Resource Manager host as its
    /// base address. Subsequent calls with the same account return the same client.
    /// </summary>
    HttpClient CreateFor(SignedInAccount account);
}
