namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Models;

/// <summary>
/// Single entry point for the UI: merges the directory-roles and PIM-for-Groups
/// surfaces, and dispatches activation/deactivation to the correct service by
/// kind. All operations are pinned to a <see cref="SignedInAccount"/>; per-account
/// service bundles are resolved by <see cref="IAccountScopedServices"/>.
/// </summary>
public interface IEligibilityAggregator
{
    /// <summary>Lists everything the given <paramref name="account"/> can activate.</summary>
    Task<IReadOnlyList<PimEligibility>> GetAllEligibilitiesAsync(
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
    /// <see cref="GetAggregatedActiveAssignmentsAsync"/> — failures on a single
    /// tenant are logged and surfaced as empty lists.
    /// </summary>
    Task<IReadOnlyDictionary<SignedInAccount, IReadOnlyList<PimEligibility>>>
        GetAggregatedEligibilitiesAsync(
            IEnumerable<SignedInAccount> accounts, CancellationToken ct = default);

    /// <summary>Activates an eligibility under <paramref name="account"/>.</summary>
    Task<ActivationResult> ActivateAsync(
        SignedInAccount account, ActivationRequest request, CancellationToken ct = default);

    /// <summary>Deactivates an active assignment under <paramref name="account"/>.</summary>
    Task<ActivationResult> DeactivateAsync(
        SignedInAccount account, ActiveAssignment assignment, CancellationToken ct = default);
}
