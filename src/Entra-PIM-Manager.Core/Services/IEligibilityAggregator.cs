namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Models;

/// <summary>
/// Single entry point for the UI: merges the directory-roles, PIM-for-Groups and
/// Azure-resource-roles surfaces, and dispatches activation/deactivation to the
/// correct service by kind. All operations are pinned to a <see cref="SignedInAccount"/>; per-account
/// service bundles are resolved by <see cref="IAccountScopedServices"/>.
/// </summary>
public interface IEligibilityAggregator
{
    /// <summary>
    /// Lists everything the given <paramref name="account"/> can activate. The
    /// Azure surface fails soft: when it cannot be read (typically a tenant that
    /// has not consented to the Azure Service Management permission) the Graph
    /// rows are still returned and <see cref="EligibilityFetchResult.LoadError"/>
    /// says why the Azure rows are missing.
    /// </summary>
    Task<EligibilityFetchResult> GetAllEligibilitiesAsync(
        SignedInAccount account, CancellationToken ct = default);

    /// <summary>Lists the active assignments for the given <paramref name="account"/>.</summary>
    Task<IReadOnlyList<ActiveAssignment>> GetAllActiveAssignmentsAsync(
        SignedInAccount account, CancellationToken ct = default);

    /// <summary>
    /// Fans out across <paramref name="accounts"/> in parallel and returns the
    /// active assignments per account. Failures on individual accounts are
    /// logged and surfaced as empty lists — a slow or broken tenant must not
    /// block the rest.
    /// </summary>
    Task<IReadOnlyDictionary<SignedInAccount, IReadOnlyList<ActiveAssignment>>>
        GetAggregatedActiveAssignmentsAsync(
            IEnumerable<SignedInAccount> accounts, CancellationToken ct = default);

    /// <summary>
    /// Fans out across <paramref name="accounts"/> in parallel and returns the
    /// eligibilities per account. Mirrors
    /// <see cref="GetAggregatedActiveAssignmentsAsync"/> — a failure on a single
    /// tenant is logged, yields an empty list and carries a user-facing
    /// <see cref="EligibilityFetchResult.LoadError"/> so the UI can say why.
    /// </summary>
    Task<IReadOnlyDictionary<SignedInAccount, EligibilityFetchResult>>
        GetAggregatedEligibilitiesAsync(
            IEnumerable<SignedInAccount> accounts, CancellationToken ct = default);

    /// <summary>Activates an eligibility under <paramref name="account"/>.</summary>
    Task<ActivationResult> ActivateAsync(
        SignedInAccount account, ActivationRequest request, CancellationToken ct = default);

    /// <summary>Deactivates an active assignment under <paramref name="account"/>.</summary>
    Task<ActivationResult> DeactivateAsync(
        SignedInAccount account, ActiveAssignment assignment, CancellationToken ct = default);

    /// <summary>
    /// Forgets the one-hour Azure backoff for <paramref name="account"/>, so the next
    /// read tries the Azure surface again instead of waiting the hour out.
    /// </summary>
    /// <remarks>
    /// The backoff is keyed by (oid, tenant, cloud) and therefore survives removing an
    /// account and signing in again — which is exactly what someone does after an admin
    /// finally granted the Azure Service Management consent. Without this, the obvious
    /// remedy silently changes nothing and the tenant stays dark until a restart.
    /// </remarks>
    void ForgetAzureBackoff(SignedInAccount account);
}
