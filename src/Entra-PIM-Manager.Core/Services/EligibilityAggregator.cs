namespace EntraPimManager.Core.Services;

using System.Collections.Concurrent;
using EntraPimManager.Core.Arm;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.ErrorHandling;
using EntraPimManager.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

/// <summary>
/// Fans out to all three PIM surfaces in parallel for reads, and dispatches
/// activation/deactivation to the correct service based on resource kind.
/// </summary>
/// <remarks>
/// All operations resolve a per-account service bundle from
/// <see cref="IAccountScopedServices"/>. The cross-tenant aggregator uses
/// <see cref="Task.WhenAll{T}"/> over independent per-account tasks, each with
/// its own timeout so a slow tenant cannot block the rest. Within an account
/// the Azure surface is isolated the same way — a tenant without the ARM
/// permission must still show its directory roles and groups.
/// </remarks>
public sealed class EligibilityAggregator : IEligibilityAggregator
{
    // Raised from 20 s after a field timeout on a healthy tenant (2026-09-04):
    // the ARM tenant-root listing pages over every scope in the estate, and a
    // single retry on a 429 already exceeded the old budget. Still well inside
    // the 60 s refresh interval.
    private static readonly TimeSpan PerAccountTimeout = TimeSpan.FromSeconds(30);

    // Inside the per-account budget: a consent prompt hanging on the ARM
    // surface must time out before it takes the Graph results down with it.
    private static readonly TimeSpan AzureSurfaceTimeout = TimeSpan.FromSeconds(25);

    // A tenant that has not consented cannot start consenting by itself, so
    // asking again every minute only burns a token call and keeps the same error
    // on screen. In-memory only — a restart tries again.
    private static readonly TimeSpan AzureBackoff = TimeSpan.FromHours(1);

    private readonly IAccountScopedServices _accountServices;
    private readonly ILogger<EligibilityAggregator> _logger;
    private readonly ConcurrentDictionary<string, (DateTimeOffset Until, string Reason)> _azureBackoff =
        new(StringComparer.OrdinalIgnoreCase);

    public EligibilityAggregator(
        IAccountScopedServices accountServices,
        ILogger<EligibilityAggregator> logger)
    {
        _accountServices = accountServices;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EligibilityFetchResult> GetAllEligibilitiesAsync(
        SignedInAccount account, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var bundle = _accountServices.GetServicesFor(account);

        var rolesTask = bundle.RoleService.GetEligibleRolesAsync(ct);
        var groupsTask = bundle.GroupService.GetEligibleGroupAccessAsync(ct);
        var azureTask = FetchAzureSafeAsync<PimEligibility>(account, bundle.AzureResourceService.GetEligibleAzureRolesAsync, ct);
        await Task.WhenAll(rolesTask, groupsTask, azureTask).ConfigureAwait(false);

        var azure = await azureTask.ConfigureAwait(false);
        return new EligibilityFetchResult(
            [.. await rolesTask.ConfigureAwait(false), .. await groupsTask.ConfigureAwait(false), .. azure.Items],
            azure.LoadError);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActiveAssignment>> GetAllActiveAssignmentsAsync(
        SignedInAccount account, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var bundle = _accountServices.GetServicesFor(account);

        var rolesTask = bundle.RoleService.GetActiveRolesAsync(ct);
        var groupsTask = bundle.GroupService.GetActiveGroupAccessAsync(ct);
        var azureTask = FetchAzureSafeAsync<ActiveAssignment>(account, bundle.AzureResourceService.GetActiveAzureRolesAsync, ct);
        await Task.WhenAll(rolesTask, groupsTask, azureTask).ConfigureAwait(false);

        // No channel for the Azure reason here — the eligibility list carries it.
        var azure = await azureTask.ConfigureAwait(false);
        return [.. await rolesTask.ConfigureAwait(false), .. await groupsTask.ConfigureAwait(false), .. azure.Items];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<SignedInAccount, ActiveAssignmentFetchResult>>
        GetAggregatedActiveAssignmentsAsync(
            IEnumerable<SignedInAccount> accounts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        var snapshot = accounts.ToList();
        if (snapshot.Count == 0)
        {
            return new Dictionary<SignedInAccount, ActiveAssignmentFetchResult>();
        }

        var tasks = snapshot
            .Select(account => FetchSafeAsync(account, FetchActiveAssignmentsAsync, "Active assignments", ct))
            .ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var dict = new Dictionary<SignedInAccount, ActiveAssignmentFetchResult>();
        for (var i = 0; i < snapshot.Count; i++)
        {
            dict[snapshot[i]] = new ActiveAssignmentFetchResult(results[i].Items, results[i].LoadError);
        }

        return dict;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<SignedInAccount, EligibilityFetchResult>>
        GetAggregatedEligibilitiesAsync(
            IEnumerable<SignedInAccount> accounts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        var snapshot = accounts.ToList();
        if (snapshot.Count == 0)
        {
            return new Dictionary<SignedInAccount, EligibilityFetchResult>();
        }

        var tasks = snapshot
            .Select(account => FetchSafeAsync(account, FetchEligibilitiesAsync, "Eligibilities", ct))
            .ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var dict = new Dictionary<SignedInAccount, EligibilityFetchResult>();
        for (var i = 0; i < snapshot.Count; i++)
        {
            dict[snapshot[i]] = new EligibilityFetchResult(results[i].Items, results[i].LoadError);
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
        return request.Eligibility.Kind switch
        {
            PimResourceKind.DirectoryRole => bundle.RoleService.ActivateAsync(request, ct),
            PimResourceKind.AzureResourceRole => bundle.AzureResourceService.ActivateAsync(request, ct),
            _ => bundle.GroupService.ActivateAsync(request, ct),
        };
    }

    /// <inheritdoc />
    public Task<ActivationResult> DeactivateAsync(
        SignedInAccount account, ActiveAssignment assignment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(assignment);
        var bundle = _accountServices.GetServicesFor(account);
        return assignment.Kind switch
        {
            PimResourceKind.DirectoryRole => bundle.RoleService.DeactivateAsync(assignment, ct),
            PimResourceKind.AzureResourceRole => bundle.AzureResourceService.DeactivateAsync(assignment, ct),
            _ => bundle.GroupService.DeactivateAsync(assignment, ct),
        };
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EligibleChildScope>> GetEligibleChildScopesAsync(
        SignedInAccount account, string scopeId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        return _accountServices.GetServicesFor(account).AzureResourceService.GetEligibleChildScopesAsync(scopeId, ct);
    }

    /// <inheritdoc />
    public void ForgetAzureBackoff(SignedInAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        _azureBackoff.TryRemove(BackoffKey(account), out _);
    }

    private static string BackoffKey(SignedInAccount account)
        => $"{account.ObjectId}|{account.TenantId}|{account.Cloud}";

    private async Task<(IReadOnlyList<PimEligibility> Items, string? LoadError)> FetchEligibilitiesAsync(
        SignedInAccount account, CancellationToken ct)
    {
        var result = await GetAllEligibilitiesAsync(account, ct).ConfigureAwait(false);
        return (result.Items, result.LoadError);
    }

    private async Task<(IReadOnlyList<ActiveAssignment> Items, string? LoadError)> FetchActiveAssignmentsAsync(
        SignedInAccount account, CancellationToken ct)
        => (await GetAllActiveAssignmentsAsync(account, ct).ConfigureAwait(false), null);

    /// <summary>
    /// Runs <paramref name="fetch"/> for a single account with a per-account
    /// timeout, logging any failure and returning it as an empty list plus a
    /// user-facing reason. Used by the cross-tenant fan-out so a slow or
    /// broken tenant cannot poison the aggregate result.
    /// </summary>
    private async Task<(IReadOnlyList<T> Items, string? LoadError)> FetchSafeAsync<T>(
        SignedInAccount account,
        Func<SignedInAccount, CancellationToken, Task<(IReadOnlyList<T> Items, string? LoadError)>> fetch,
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
            return (Array.Empty<T>(), PimErrorMapper.DescribeFetchFailure(ex, account.AuthMethod));
        }
    }

    /// <summary>
    /// Runs one Azure Resource Manager read with its own timeout and turns its
    /// failure into a user-facing reason instead of an exception, so the Graph
    /// surfaces of the same account are unaffected. A failed token acquisition
    /// (consent missing, prompt dismissed) or a timed-out prompt puts the
    /// enrollment on <see cref="AzureBackoff"/>; an ordinary ARM error is
    /// simply retried on the next refresh.
    /// </summary>
    private async Task<(IReadOnlyList<T> Items, string? LoadError)> FetchAzureSafeAsync<T>(
        SignedInAccount account,
        Func<CancellationToken, Task<IReadOnlyList<T>>> fetch,
        CancellationToken ct)
    {
        var key = BackoffKey(account);
        if (_azureBackoff.TryGetValue(key, out var backoff) && backoff.Until > DateTimeOffset.UtcNow)
        {
            return (Array.Empty<T>(), backoff.Reason);
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(AzureSurfaceTimeout);
            return (await fetch(cts.Token).ConfigureAwait(false), null);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Azure resource roles fetch failed for account oid {ObjectId} tenant {TenantId}",
                account.ObjectId,
                account.TenantId);

            var reason = "Azure resource roles unavailable: " + (ex is OperationCanceledException
                ? "the request timed out."
                : PimErrorMapper.DescribeFetchFailure(ex, account.AuthMethod));

            // Only a failed sign-in earns the hour. Anything cancelled — a slow read on
            // a large estate, a token call that didn't finish — may well succeed on the
            // next tick, and an hour without Azure roles is the worse answer by far.
            if (ex is MsalException)
            {
                _azureBackoff[key] = (DateTimeOffset.UtcNow + AzureBackoff, reason);
            }

            return (Array.Empty<T>(), reason);
        }
    }
}
