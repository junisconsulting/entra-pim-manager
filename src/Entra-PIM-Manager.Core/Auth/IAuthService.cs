namespace EntraPimManager.Core.Auth;

using Microsoft.Identity.Client;

/// <summary>
/// Authentication facade over MSAL + the WAM broker. All token acquisition for
/// Entra PIM Manager goes through this service.
/// </summary>
/// <remarks>
/// Multi-account: the service supports several enrolled identities at once,
/// each addressed by the (oid, tenantId, cloud) tuple of a
/// <see cref="SignedInAccount"/>.
/// </remarks>
public interface IAuthService
{
    /// <summary>
    /// Drives an interactive WAM sign-in (account picker) targeting the chosen
    /// identity's home tenant, persists the result to the <see cref="IAccountStore"/>,
    /// and returns it.
    /// </summary>
    Task<SignedInAccount> AddAccountAsync(CancellationToken ct = default);

    /// <summary>
    /// Drives an interactive WAM sign-in targeting <paramref name="tenantIdOrDomain"/>
    /// in <paramref name="cloud"/> — used to enroll the same identity against a
    /// guest/secondary tenant, including a sovereign cloud (e.g. China). The
    /// returned <see cref="SignedInAccount"/> carries the requested tenant's id
    /// and cloud; the underlying MSAL <c>IAccount</c> is the user's home identity
    /// in the chosen cloud, reused across tenant enrollments within that cloud.
    /// </summary>
    Task<SignedInAccount> AddAccountForTenantAsync(
        string tenantIdOrDomain,
        EntraCloud cloud,
        CancellationToken ct = default);

    /// <summary>
    /// Removes the enrollment matching (<paramref name="objectId"/>, <paramref name="tenantId"/>)
    /// from the <see cref="IAccountStore"/>. The MSAL cache entry for the underlying
    /// home identity is only purged when no other tenant enrollment within the same
    /// <paramref name="cloud"/> still uses it.
    /// </summary>
    Task RemoveAccountAsync(
        string objectId,
        string tenantId,
        EntraCloud cloud,
        CancellationToken ct = default);

    /// <summary>Returns all enrolled accounts in stable order.</summary>
    Task<IReadOnlyList<SignedInAccount>> GetAllAccountsAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists the enrolled accounts in the given order. No MSAL state is
    /// touched — only the <see cref="IAccountStore"/> ordering.
    /// </summary>
    Task ReorderAccountsAsync(IReadOnlyList<SignedInAccount> orderedAccounts, CancellationToken ct = default);

    /// <summary>
    /// Acquires an access token for the enrollment identified by
    /// (<paramref name="objectId"/>, <paramref name="tenantId"/>, <paramref name="cloud"/>) —
    /// silent first, falling back to an interactive WAM prompt pinned to the home
    /// MSAL account in the matching cloud, with the request scoped to
    /// <paramref name="tenantId"/> via <c>.WithTenantId</c>. When
    /// <paramref name="claimsChallenge"/> is supplied, the token is re-requested
    /// to satisfy a Conditional Access challenge.
    /// </summary>
    Task<AuthenticationResult> AcquireTokenForAccountAsync(
        string objectId,
        string tenantId,
        EntraCloud cloud,
        string[] scopes,
        string? claimsChallenge = null,
        CancellationToken ct = default);
}
