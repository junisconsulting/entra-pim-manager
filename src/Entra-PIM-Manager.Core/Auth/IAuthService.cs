namespace EntraPimManager.Core.Auth;

using Microsoft.Identity.Client;

/// <summary>
/// Authentication facade over MSAL + the WAM broker. All token acquisition for
/// Entra PIM Manager goes through this service.
/// </summary>
/// <remarks>
/// Multi-account: the service supports several enrolled identities at once,
/// each addressed by the (oid, tenantId, cloud) tuple of a
/// <see cref="SignedInAccount"/>. Which App Registration a (cloud, tenant) pair
/// authenticates against is decided by
/// <see cref="Configuration.EntraPimManagerOptions.ClientIdFor"/> — a tenant
/// without a registration cannot be enrolled.
/// </remarks>
public interface IAuthService
{
    /// <summary>
    /// Drives an interactive WAM sign-in to <paramref name="tenantId"/> in
    /// <paramref name="cloud"/> through the registration pinned to that tenant,
    /// persists the result to the <see cref="IAccountStore"/>, and returns it.
    /// </summary>
    /// <remarks>
    /// The returned <see cref="SignedInAccount"/> carries the tenant's id and cloud;
    /// the underlying MSAL <c>IAccount</c> is the user's home identity, reused across
    /// tenant enrollments that share a registration. Fails with
    /// <c>app_registration_missing</c> when no registration is pinned to the tenant.
    /// </remarks>
    Task<SignedInAccount> AddAccountAsync(
        string tenantId,
        EntraCloud cloud,
        CancellationToken ct = default);

    /// <summary>
    /// Enrolls an account via the OAuth 2.0 device-code flow instead of the WAM
    /// broker. <paramref name="onChallenge"/> is invoked once with the user code
    /// and verification URL to display; the returned task completes after the
    /// user finishes sign-in on a second device (or faults/cancels).
    /// </summary>
    /// <remarks>
    /// This is the escape hatch for tenants whose external IdP (e.g. Okta) does
    /// aggressive seamless SSO and would otherwise bind the app to the Windows
    /// session's Office identity. Completing sign-in on a phone decouples the
    /// federated leg from this machine's browser session. Note: device-code flow
    /// runs broker-less, so Conditional Access policies that require a compliant
    /// device — or that block device-code flow outright — will reject it. The
    /// resulting account is persisted with <see cref="Auth.AuthMethod.DeviceCode"/>
    /// so token renewal stays on the broker-less path.
    /// </remarks>
    Task<SignedInAccount> AddAccountViaDeviceCodeAsync(
        string tenantId,
        EntraCloud cloud,
        Func<DeviceCodeChallenge, Task> onChallenge,
        CancellationToken ct = default);

    /// <summary>
    /// Removes the enrollment matching (<paramref name="objectId"/>, <paramref name="tenantId"/>)
    /// from the <see cref="IAccountStore"/>. The MSAL cache entry for the underlying
    /// home identity is only purged when no other tenant enrollment resolving to the
    /// same App Registration still uses it.
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
    /// MSAL account under the App Registration that (cloud, tenant) resolves to, with
    /// the request scoped to <paramref name="tenantId"/> via <c>.WithTenantId</c>. When
    /// <paramref name="claimsChallenge"/> is supplied, the token is re-requested
    /// to satisfy a Conditional Access challenge.
    /// </summary>
    /// <param name="objectId">Object id of the enrolled user.</param>
    /// <param name="tenantId">Tenant the token is requested for.</param>
    /// <param name="cloud">Sovereign cloud the enrollment lives in.</param>
    /// <param name="scopes">Scopes to request. Graph and ARM scopes are never mixed.</param>
    /// <param name="claimsChallenge">Decoded claims JSON from a Conditional Access challenge, or null.</param>
    /// <param name="silentOnly">
    /// True to fail instead of opening a WAM prompt. Background reads pass this: the
    /// interactive fallback runs while a process-wide lock is held, so an unanswered
    /// prompt stalls every other account's token renewal until the caller's timeout —
    /// and for a scope the tenant has not consented to, the prompt cannot succeed
    /// anyway unless the user may self-consent. A user-initiated activation leaves it
    /// false, because a Conditional Access step-up is exactly what the prompt is for.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<AuthenticationResult> AcquireTokenForAccountAsync(
        string objectId,
        string tenantId,
        EntraCloud cloud,
        string[] scopes,
        string? claimsChallenge = null,
        bool silentOnly = false,
        CancellationToken ct = default);
}
