namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fans out to both PIM surfaces in parallel for reads, and dispatches
/// activation/deactivation to the correct service based on resource kind.
/// </summary>
/// <remarks>
/// All operations resolve a per-account service bundle from
/// <see cref="IAccountScopedServices"/>. The cross-tenant aggregator uses
/// <see cref="Task.WhenAll{T}"/> over independent per-account tasks, each with
/// its own timeout so a slow tenant cannot block the rest.
/// </remarks>
public sealed class EligibilityAggregator : IEligibilityAggregator
{
    private static readonly TimeSpan PerAccountTimeout = TimeSpan.FromSeconds(20);

    private readonly IAccountScopedServices _accountServices;
    private readonly ILogger<EligibilityAggregator> _logger;

    public EligibilityAggregator(
        IAccountScopedServices accountServices,
        ILogger<EligibilityAggregator> logger)
    {
        _accountServices = accountServices;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PimEligibility>> GetAllEligibilitiesAsync(
        SignedInAccount account, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var bundle = _accountServices.GetServicesFor(account);
        return FetchAllEligibilitiesAsync(bundle.RoleService, bundle.GroupService, ct);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ActiveAssignment>> GetAllActiveAssignmentsAsync(
        SignedInAccount account, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var bundle = _accountServices.GetServicesFor(account);
        return FetchAllActiveAssignmentsAsync(bundle.RoleService, bundle.GroupService, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<SignedInAccount, IReadOnlyList<ActiveAssignment>>>
        GetAggregatedActiveAssignmentsAsync(
            IEnumerable<SignedInAccount> accounts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        var snapshot = accounts.ToList();
        if (snapshot.Count == 0)
        {
            return new Dictionary<SignedInAccount, IReadOnlyList<ActiveAssignment>>();
        }

        var tasks = snapshot
            .Select(account => FetchSafeAsync(account, GetAllActiveAssignmentsAsync, "Active assignments", ct))
            .ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var dict = new Dictionary<SignedInAccount, IReadOnlyList<ActiveAssignment>>();
        for (var i = 0; i < snapshot.Count; i++)
        {
            dict[snapshot[i]] = results[i];
        }

        return dict;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<SignedInAccount, IReadOnlyList<PimEligibility>>>
        GetAggregatedEligibilitiesAsync(
            IEnumerable<SignedInAccount> accounts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        var snapshot = accounts.ToList();
        if (snapshot.Count == 0)
        {
            return new Dictionary<SignedInAccount, IReadOnlyList<PimEligibility>>();
        }

        var tasks = snapshot
            .Select(account => FetchSafeAsync(account, GetAllEligibilitiesAsync, "Eligibilities", ct))
            .ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var dict = new Dictionary<SignedInAccount, IReadOnlyList<PimEligibility>>();
        for (var i = 0; i < snapshot.Count; i++)
        {
            dict[snapshot[i]] = results[i];
        }

        return dict;
    }

    /// <inheritdoc />
    public Task<ActivationResult> ActivateAsync(
        SignedInAccount account, ActivationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(request);
        var bundle = _accountServices.GetServicesFor(account);
        return request.Eligibility.Kind == PimResourceKind.DirectoryRole
            ? bundle.RoleService.ActivateAsync(request, ct)
            : bundle.GroupService.ActivateAsync(request, ct);
    }

    /// <inheritdoc />
    public Task<ActivationResult> DeactivateAsync(
        SignedInAccount account, ActiveAssignment assignment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(assignment);
        var bundle = _accountServices.GetServicesFor(account);
        return assignment.Kind == PimResourceKind.DirectoryRole
            ? bundle.RoleService.DeactivateAsync(assignment, ct)
            : bundle.GroupService.DeactivateAsync(assignment, ct);
    }

    private static async Task<IReadOnlyList<PimEligibility>> FetchAllEligibilitiesAsync(
        IPimRoleService roleService, IPimGroupService groupService, CancellationToken ct)
    {
        var rolesTask = roleService.GetEligibleRolesAsync(ct);
        var groupsTask = groupService.GetEligibleGroupAccessAsync(ct);
        await Task.WhenAll(rolesTask, groupsTask).ConfigureAwait(false);

        return [.. await rolesTask.ConfigureAwait(false), .. await groupsTask.ConfigureAwait(false)];
    }

    private static async Task<IReadOnlyList<ActiveAssignment>> FetchAllActiveAssignmentsAsync(
        IPimRoleService roleService, IPimGroupService groupService, CancellationToken ct)
    {
        var rolesTask = roleService.GetActiveRolesAsync(ct);
        var groupsTask = groupService.GetActiveGroupAccessAsync(ct);
        await Task.WhenAll(rolesTask, groupsTask).ConfigureAwait(false);

        return [.. await rolesTask.ConfigureAwait(false), .. await groupsTask.ConfigureAwait(false)];
    }

    /// <summary>
    /// Runs <paramref name="fetch"/> for a single account with a per-account
    /// timeout, logging and swallowing any failure as an empty list. Used by
    /// the cross-tenant fan-out so a slow or broken tenant cannot poison the
    /// aggregate result.
    /// </summary>
    private async Task<IReadOnlyList<T>> FetchSafeAsync<T>(
        SignedInAccount account,
        Func<SignedInAccount, CancellationToken, Task<IReadOnlyList<T>>> fetch,
        string fetchLabel,
        CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(PerAccountTimeout);
            return await fetch(account, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "{Label} fetch failed for account oid {ObjectId} tenant {TenantId}",
                fetchLabel,
                account.ObjectId,
                account.TenantId);
            return Array.Empty<T>();
        }
    }
}
