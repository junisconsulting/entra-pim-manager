namespace EntraPimManager.AppAvalonia.ViewModels;

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EntraPimManager.AppAvalonia.Services;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;
using EntraPimManager.Core.ErrorHandling;
using EntraPimManager.Core.Models;
using EntraPimManager.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Central application view model. Drives a multi-tenant shell:
///
/// <list type="bullet">
/// <item><b>Cross-tenant ACTIVE bar</b> — <see cref="ActiveAssignments"/> aggregates
/// active roles across every enrolled account; each row carries a tenant label.</item>
/// <item><b>Cross-tenant ELIGIBILITIES list</b> — <see cref="EligibilityGroups"/>
/// groups eligibilities by tenant so the user sees everything they could activate
/// without context-switching. Each group is collapsible; filter auto-expands
/// matching groups.</item>
/// <item><b>Slide-in activation</b> — the activation panel knows which account it
/// activates under (routed from the clicked eligibility's <see cref="EligibilityItemViewModel.Account"/>).</item>
/// </list>
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IAccountsHost
{
    private const string GitHubProjectUrl = "https://github.com/junisconsulting/entra-pim-manager";

    // Cap on parallel policy prefetch requests. Microsoft Graph PIM endpoints
    // have global throttling — 6 in flight at once is enough to feel snappy
    // without risking 429s.
    private const int PolicyPrefetchConcurrency = 6;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan GraphCallTimeout = TimeSpan.FromSeconds(30);

    // How long an "Activating…" placeholder may live without Graph publishing
    // the real assignment before we give up and drop it.
    private static readonly TimeSpan PendingWatchdog = TimeSpan.FromSeconds(30);

    private readonly IAuthService _authService;
    private readonly IEligibilityAggregator _aggregator;
    private readonly IAccountScopedServices _accountServices;
    private readonly ITenantInfoService _tenantInfoService;
    private readonly IToastService _toastService;
    private readonly IUserSettingsService _userSettings;
    private readonly EntraPimManagerOptions _options;
    private readonly ActivationPanelViewModel _activationPanel;
    private readonly AddTenantPanelViewModel _addTenantPanel;
    private readonly SettingsPanelViewModel _settingsPanel;
    private readonly ILogger<ShellViewModel> _logger;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _countdownTimer;

    // Tenant display name is per-tenant, not per-account: multiple enrolled
    // accounts in the same tenant share one entry. Negative results are cached too.
    private readonly Dictionary<string, string?> _tenantNameCache = new(StringComparer.OrdinalIgnoreCase);

    // Snapshot of per-group IsExpanded state taken at the moment a filter
    // becomes active, restored when the user clears the filter. Without this
    // the auto-expand-on-match behaviour would overwrite the layout the user
    // had before they started typing.
    private Dictionary<string, bool>? _preFilterExpansion;

    // Cancellation handle for the background policy prefetch. Each new
    // RefreshAsync cancels the previous prefetch (which is likely working
    // on stale data anyway) before starting a fresh one.
    private CancellationTokenSource? _prefetchCts;

    [ObservableProperty]
    private bool _isSignedIn;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private SignedInAccount? _activeAccount;

    [ObservableProperty]
    private int _activeCount;

    [ObservableProperty]
    private int _eligibleCount;

    public ShellViewModel(
        IAuthService authService,
        IEligibilityAggregator aggregator,
        IAccountScopedServices accountServices,
        ITenantInfoService tenantInfoService,
        IToastService toastService,
        IUserSettingsService userSettings,
        IOptions<EntraPimManagerOptions> options,
        ActivationPanelViewModel activationPanel,
        AddTenantPanelViewModel addTenantPanel,
        SettingsPanelViewModel settingsPanel,
        ILogger<ShellViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _authService = authService;
        _aggregator = aggregator;
        _accountServices = accountServices;
        _tenantInfoService = tenantInfoService;
        _toastService = toastService;
        _userSettings = userSettings;
        _options = options.Value;
        _activationPanel = activationPanel;
        _addTenantPanel = addTenantPanel;
        _settingsPanel = settingsPanel;
        _logger = logger;

        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => UpdateCountdowns();

        _activationPanel.Closed += OnActivationPanelClosed;
        _addTenantPanel.Closed += OnAddTenantPanelClosed;

        // Late-binding: Settings was constructed first by DI (it has no
        // dependency on Shell), so the host has to be plugged in here once
        // both view models exist. See IAccountsHost for the rationale.
        _settingsPanel.AttachAccountsHost(this);

        // HasNoAccounts / ShowEligibilityUi are computed off the Accounts
        // collection — re-raise both so the empty-state CTA + the
        // search/list IsVisible bindings flip correctly on add/remove.
        Accounts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNoAccounts));
            OnPropertyChanged(nameof(ShowEligibilityUi));
        };
    }

    /// <summary>Raised when <see cref="ActiveCount"/> changes — the tray icon listens.</summary>
    public event EventHandler? ActiveCountChanged;

    /// <summary>
    /// Enrolled accounts in stable order, wrapped so the row can carry a
    /// mutable tenant label. Bound to the Settings ACCOUNTS section.
    /// </summary>
    public ObservableCollection<AccountListItemViewModel> Accounts { get; } = [];

    /// <summary>Eligibilities grouped per enrolled account / tenant.</summary>
    public ObservableCollection<TenantEligibilityGroup> EligibilityGroups { get; } = [];

    /// <summary>Cross-tenant: active assignments across every enrolled account.</summary>
    public ObservableCollection<ActiveAssignmentItemViewModel> ActiveAssignments { get; } = [];

    /// <summary>The slide-in activation panel — bound by the popup window.</summary>
    public ActivationPanelViewModel ActivationPanel => _activationPanel;

    /// <summary>The slide-in "connect to additional tenant" panel — bound by the popup window.</summary>
    public AddTenantPanelViewModel AddTenantPanel => _addTenantPanel;

    /// <summary>The slide-in Settings panel — bound by the popup window.</summary>
    public SettingsPanelViewModel SettingsPanel => _settingsPanel;

    /// <inheritdoc />
    IAsyncRelayCommand<SignedInAccount?> IAccountsHost.RemoveAccountCommand => RemoveAccountCommand;

    /// <inheritdoc />
    IRelayCommand IAccountsHost.OpenAddAccountPanelCommand => OpenAddAccountPanelCommand;

    /// <inheritdoc />
    IRelayCommand<SignedInAccount?> IAccountsHost.SelectAccountCommand => SelectAccountCommand;

    /// <summary>
    /// True when the App Registration ClientId hasn't been configured yet —
    /// either empty or a non-GUID placeholder. The main popup shows a
    /// first-run empty state pointing the user at Settings → App Registration;
    /// the regular eligibility / active lists stay hidden until this is false.
    /// </summary>
    public bool NeedsConfiguration =>
        string.IsNullOrWhiteSpace(_options.ClientId)
        || !Guid.TryParse(_options.ClientId, out _);

    /// <summary>True when configuration is valid but no account is enrolled yet.</summary>
    public bool HasNoAccounts => !NeedsConfiguration && Accounts.Count == 0;

    /// <summary>
    /// True when the regular eligibility UI (search bar + grouped list) should
    /// be shown — i.e. configuration is complete and at least one account is
    /// enrolled. Mutually exclusive with the two empty-state CTAs.
    /// </summary>
    public bool ShowEligibilityUi => !NeedsConfiguration && Accounts.Count > 0;

    /// <summary>
    /// Composed label for the title-strip stats. Single source of truth so
    /// idle ↔ loading caption swap can be done with a single binding pair.
    /// </summary>
    public string StatsLabel => $"{EligibleCount} eligible · {ActiveCount} active";

    /// <summary>Footer version label, e.g. <c>v1.0.0</c>. Reads the entry assembly's version.</summary>
    public string VersionText
    {
        get
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            return version is null ? "v?" : $"v{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    /// <summary>Loads persisted accounts and refreshes if any are present.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (NeedsConfiguration)
        {
            // First-run: the App Registration ClientId hasn't been set yet.
            // Skip account/auth init entirely — the empty-state CTA in the
            // popup will guide the user to Settings → App Registration.
            _countdownTimer.Start();
            return;
        }

        try
        {
            var accounts = await _authService.GetAllAccountsAsync(ct);
            ReplaceAccounts(accounts);

            // Restore last-used account if it's still enrolled. ReplaceAccounts
            // already set ActiveAccount to the first enrollment as a fallback;
            // override it here so the user's actual most-recent choice wins.
            if (_userSettings.Current.LastUsedAccountKey is { Length: > 0 } lastKey)
            {
                var match = Accounts
                    .FirstOrDefault(a => EnrollmentKey(a.Account) == lastKey)?.Account;
                if (match is not null)
                {
                    ActiveAccount = match;
                }
            }

            if (ActiveAccount is not null)
            {
                IsSignedIn = true;
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Shell initialization failed");
        }

        _countdownTimer.Start();
    }

    /// <summary>
    /// Reorders the enrolled accounts so <paramref name="draggedAccount"/>
    /// lands at <paramref name="newIndex"/>. Used by the DnD wiring in the
    /// Settings ACCOUNTS section. The change propagates to:
    /// <list type="bullet">
    /// <item><see cref="Accounts"/> — moved in place (ObservableCollection.Move
    /// keeps the existing item instances)</item>
    /// <item><see cref="EligibilityGroups"/> — reordered locally to match
    /// without re-fetching from Graph; the existing group VMs (with their
    /// IsExpanded state) are preserved</item>
    /// <item>Persistence — full ordered list is written to
    /// <see cref="IAuthService.ReorderAccountsAsync"/></item>
    /// </list>
    /// </summary>
    public async Task MoveAccountAsync(SignedInAccount draggedAccount, int newIndex)
    {
        if (draggedAccount is null)
        {
            return;
        }

        var oldIndex = -1;
        for (var i = 0; i < Accounts.Count; i++)
        {
            if (IsSameEnrollment(Accounts[i].Account, draggedAccount))
            {
                oldIndex = i;
                break;
            }
        }

        if (oldIndex < 0 || newIndex < 0 || newIndex >= Accounts.Count || newIndex == oldIndex)
        {
            return;
        }

        Accounts.Move(oldIndex, newIndex);
        RebuildGroupsFromAccountOrder();

        try
        {
            await _authService.ReorderAccountsAsync(
                Accounts.Select(a => a.Account).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist account reorder");
        }
    }

    /// <summary>Refreshes eligibilities and active assignments across all enrolled accounts.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (Accounts.Count == 0 || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(GraphCallTimeout);
            var snapshot = Accounts.Select(a => a.Account).ToList();

            // Both reads fan out across all enrolled accounts in parallel with
            // per-account timeouts — a slow or broken tenant cannot block the
            // rest.
            var activeTask = _aggregator.GetAggregatedActiveAssignmentsAsync(snapshot, cts.Token);
            var eligibilityTask = _aggregator.GetAggregatedEligibilitiesAsync(snapshot, cts.Token);
            await Task.WhenAll(activeTask, eligibilityTask);

            UpdateActiveAssignments(activeTask.Result);
            BuildEligibilityGroups(eligibilityTask.Result);
            MarkActiveEligibilities();
            ApplyFilter();
            _refreshTimer.Start();

            // Background-warm the per-eligibility policy cache so the first
            // click on a role doesn't sit on a 1-3s Graph round-trip. The
            // call is fire-and-forget and uses its own CTS so the next
            // refresh-tick cancels stale prefetches before starting fresh.
            KickOffPolicyPrefetch();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveAccountAsync(SignedInAccount? account)
    {
        if (account is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _authService.RemoveAccountAsync(account.ObjectId, account.TenantId, account.Cloud);

            var item = Accounts.FirstOrDefault(a => IsSameEnrollment(a.Account, account));
            if (item is not null)
            {
                Accounts.Remove(item);
            }

            if (ActiveAccount is { } current && IsSameEnrollment(current, account))
            {
                ActiveAccount = Accounts.FirstOrDefault()?.Account;
            }

            // Drop the removed enrollment's group + active rows. Other
            // enrollments of the same identity in different tenants stay.
            var staleGroup = EligibilityGroups
                .FirstOrDefault(g => IsSameEnrollment(g.Account, account));
            if (staleGroup is not null)
            {
                EligibilityGroups.Remove(staleGroup);
            }

            var staleActive = ActiveAssignments
                .Where(a => IsSameEnrollment(a.Account, account))
                .ToList();
            foreach (var row in staleActive)
            {
                ActiveAssignments.Remove(row);
            }

            UpdateActiveCount();
            UpdateEligibleCount();
            IsSignedIn = ActiveAccount is not null;
            if (Accounts.Count == 0)
            {
                _refreshTimer.Stop();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Remove account failed");
            _toastService.ShowError("Remove account", PimErrorMapper.MapException(ex).Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Opens the single "Add account" slide-in (broker sign-in primary,
    /// device code under Advanced). Closes Settings first: the slide-in panels
    /// are overlapping siblings and Settings renders on top, so otherwise this
    /// panel opens invisibly behind it.</summary>
    [RelayCommand]
    private void OpenAddAccountPanel()
    {
        _settingsPanel.IsOpen = false;
        _addTenantPanel.Open();
    }

    /// <summary>Opens the Settings slide-in. Closes any other open slide-in first
    /// so the panel slots stay mutually exclusive. Sets <c>IsOpen=false</c>
    /// directly on the other panels to slide them out without firing their
    /// <c>Closed</c> events (which would re-run activation / tenant-add side
    /// effects).</summary>
    [RelayCommand]
    private void OpenSettingsPanel()
    {
        _activationPanel.IsOpen = false;
        _addTenantPanel.IsOpen = false;
        _settingsPanel.Open();
    }

    /// <summary>Selects an enrolled account as the active context. After the
    /// HERO removal the only behavioural consequence is which group is
    /// expanded by default; the eligibility list itself is global.</summary>
    [RelayCommand]
    private void SelectAccount(SignedInAccount? account)
    {
        if (account is null)
        {
            return;
        }

        ActiveAccount = account;
    }

    /// <summary>
    /// Cancels any in-flight policy prefetch and starts a fresh one for the
    /// current <see cref="EligibilityGroups"/> snapshot. Must be called on
    /// the UI thread (it reads from the observable collections).
    /// </summary>
    private void KickOffPolicyPrefetch()
    {
        _prefetchCts?.Cancel();
        _prefetchCts?.Dispose();
        _prefetchCts = new CancellationTokenSource();

        var targets = EligibilityGroups
            .SelectMany(g => g.Items.Select(i => (g.Account, i.Eligibility)))
            .ToList();
        if (targets.Count == 0)
        {
            return;
        }

        _ = PrefetchPoliciesAsync(targets, _prefetchCts.Token);
    }

    /// <summary>
    /// Background-warms the <see cref="EntraPimManager.Core.Caching.PolicyCache"/>
    /// for every visible eligibility. <see cref="IPolicyService.GetPolicyAsync"/>
    /// checks its own cache first, so this is a Graph round-trip only for
    /// policies that are missing or past their TTL. Concurrency is capped
    /// at <see cref="PolicyPrefetchConcurrency"/> so we don't risk Graph 429
    /// throttling on cold start with many eligibilities.
    /// </summary>
    private async Task PrefetchPoliciesAsync(
        IReadOnlyList<(SignedInAccount Account, PimEligibility Eligibility)> targets,
        CancellationToken ct)
    {
        try
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = PolicyPrefetchConcurrency,
                CancellationToken = ct,
            };

            await Parallel.ForEachAsync(targets, options, async (target, innerCt) =>
            {
                try
                {
                    var bundle = _accountServices.GetServicesFor(target.Account);
                    await bundle.PolicyService.GetPolicyAsync(
                        target.Account.TenantId,
                        target.Eligibility.Kind,
                        target.Eligibility.ResourceId,
                        innerCt).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when the next refresh-tick cancels us.
                }
                catch (Exception ex)
                {
                    // Single-target failure must not break the loop — the
                    // user falls back to the lazy fetch in ActivateAsync,
                    // which raises a toast if it also fails.
                    _logger.LogDebug(
                        ex,
                        "Policy prefetch failed for {Role} in tenant {TenantId}",
                        target.Eligibility.DisplayName,
                        target.Account.TenantId);
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Policy prefetch loop terminated unexpectedly");
        }
    }

    /// <summary>
    /// Re-shuffles <see cref="EligibilityGroups"/> to match the current
    /// <see cref="Accounts"/> order. Reuses the existing group instances so
    /// their <c>IsExpanded</c> / <c>Items</c> state isn't lost — a drag-reorder
    /// must not trigger a Graph round-trip.
    /// </summary>
    private void RebuildGroupsFromAccountOrder()
    {
        var existing = EligibilityGroups
            .ToDictionary(g => EnrollmentKey(g.Account), g => g);

        EligibilityGroups.Clear();
        foreach (var accountItem in Accounts)
        {
            if (existing.TryGetValue(EnrollmentKey(accountItem.Account), out var group))
            {
                EligibilityGroups.Add(group);
            }
        }
    }

    /// <summary>Opens the GitHub project page in the user's default browser.</summary>
    [RelayCommand]
    private void OpenGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo(GitHubProjectUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open GitHub project page");
        }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnEligibleCountChanged(int value) => OnPropertyChanged(nameof(StatsLabel));

    partial void OnActiveCountChanged(int value) => OnPropertyChanged(nameof(StatsLabel));

    /// <summary>
    /// Active-account change: expand the matching group so the user's primary
    /// tenant is open by default, and persist the new <c>LastUsedAccountKey</c>
    /// so the choice survives an app restart.
    /// </summary>
    partial void OnActiveAccountChanged(SignedInAccount? value)
    {
        if (value is null)
        {
            return;
        }

        ExpandGroupFor(value);

        var key = EnrollmentKey(value);
        if (_userSettings.Current.LastUsedAccountKey != key)
        {
            PersistShellSettings(s => s with { LastUsedAccountKey = key });
        }
    }

    private async Task ActivateAsync(EligibilityItemViewModel item)
    {
        // Route to the eligibility's own account — not ActiveAccount. The
        // user may be activating something from a collapsed tenant group
        // they don't have "selected" in the new model.
        var account = item.Account;

        // Mark the row as activating so the chevron swaps for a spinner —
        // the policy fetch can take ~1-3s on a cold cache (first click after
        // app start) and without feedback the click feels swallowed. The
        // background prefetch usually has the policy ready by the time the
        // user clicks, but this guards against the edge case where the
        // user is faster than the prefetch.
        item.IsActivating = true;
        try
        {
            using var cts = new CancellationTokenSource(GraphCallTimeout);
            var bundle = _accountServices.GetServicesFor(account);
            var policy = await bundle.PolicyService.GetPolicyAsync(
                account.TenantId,
                item.Eligibility.Kind,
                item.Eligibility.ResourceId,
                cts.Token);
            _settingsPanel.IsOpen = false;

            // Switch the "default" account to the one the user is acting on
            // so the Phase 4 LastUsedAccount persistence captures intent.
            if (!IsSameEnrollment(ActiveAccount, account))
            {
                ActiveAccount = account;
            }

            _activationPanel.Open(account, item.Eligibility, policy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading policy failed");
            _toastService.ShowError("Activation", PimErrorMapper.MapException(ex).Message);
        }
        finally
        {
            item.IsActivating = false;
        }
    }

    private async Task DeactivateAsync(ActiveAssignmentItemViewModel item)
    {
        if (item.IsDeactivating)
        {
            return;
        }

        item.IsDeactivating = true;
        var deactivationKey = PendingMatchKey(
            item.Account,
            item.Assignment.Kind,
            item.Assignment.ResourceId,
            item.Assignment.ScopeId);
        SetDeactivationErrorText(deactivationKey, null);

        try
        {
            // Single attempt: the row's 10 min post-activation lockout
            // (ActiveAssignmentItemViewModel.IsInProvisioningWindow) already
            // prevents the dominant failure mode (PIM eventual-consistency
            // returning HTTP 201 + status=Failed during cross-service
            // provisioning). If Graph still rejects after the lockout, the
            // problem is structural — better to surface it fast than to sit
            // on a retry-loop that wouldn't help.
            using var cts = new CancellationTokenSource(GraphCallTimeout);
            var result = await _aggregator.DeactivateAsync(item.Account, item.Assignment, cts.Token);

            _logger.LogInformation(
                "Deactivation request submitted for {Role} (account oid {Oid}): requestId={RequestId}, status={Status}",
                item.DisplayName,
                item.Account.ObjectId,
                result.RequestId,
                result.Status);

            if (result.Error is not null
                || result.Status is ActivationStatus.Failed or ActivationStatus.Denied)
            {
                // Surface the failure both as a toast AND inline on the row
                // — Windows may swallow the toast under Focus Assist / DND,
                // the row label is the user's persistent fallback signal.
                var detail = result.Error?.Message
                    ?? $"{item.DisplayName}: Microsoft Graph reported status {result.Status}.";
                _toastService.ShowError("Deactivation failed", detail);
                SetDeactivationErrorText(
                    deactivationKey,
                    "Deactivation failed — try again in a moment.");
                ClearDeactivatingState(deactivationKey);
                return;
            }

            // Microsoft's read API is eventually consistent — the deactivated
            // assignment can take 5–60 s to drop off /roleAssignmentScheduleInstances.
            // Poll up to ~60 s; only fire the success toast once the role has
            // actually disappeared so we never falsely claim success.
            const int maxPollAttempts = 10;
            for (var pollAttempt = 1; pollAttempt <= maxPollAttempts; pollAttempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(6));
                await RefreshAsync();

                if (!ActiveAssignmentExists(deactivationKey))
                {
                    _toastService.ShowDeactivationResult(item.DisplayName, result);
                    return;
                }
            }

            // Watchdog: still active after ~60 s. Surface the Graph status so
            // the user can distinguish "PIM never processed it" from "PIM
            // accepted but the read API is just lagging".
            ClearDeactivatingState(deactivationKey);
            _toastService.ShowError(
                "Deactivation not confirmed",
                $"{item.DisplayName} still appears active after {maxPollAttempts * 6}s (Graph status: {result.Status}). Check the Entra PIM portal.");
            SetDeactivationErrorText(
                deactivationKey,
                $"Still active after {maxPollAttempts * 6}s — check the Entra PIM portal.");
            _logger.LogWarning(
                "Deactivation watchdog: {Role} still active {Seconds}s after request (account oid {Oid}, requestId {RequestId}, status {Status})",
                item.DisplayName,
                maxPollAttempts * 6,
                item.Account.ObjectId,
                result.RequestId,
                result.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deactivation failed");
            var mapped = PimErrorMapper.MapException(ex).Message;
            _toastService.ShowError("Deactivation failed", mapped);
            SetDeactivationErrorText(deactivationKey, $"Deactivation failed — {mapped}");
            ClearDeactivatingState(deactivationKey);
        }
    }

    /// <summary>True while any row whose composite key matches <paramref name="key"/>
    /// is still in <see cref="ActiveAssignments"/>. Used by the deactivation
    /// watchdog to decide whether the deactivation has taken effect.</summary>
    private bool ActiveAssignmentExists(
        (PimResourceKind Kind, string ResourceId, string ScopeId, string ObjectId, string TenantId) key)
        => ActiveAssignments.Any(a =>
            PendingMatchKey(a.Account, a.Assignment.Kind, a.Assignment.ResourceId, a.Assignment.ScopeId).Equals(key));

    /// <summary>Clears <see cref="ActiveAssignmentItemViewModel.IsDeactivating"/>
    /// on every row matching <paramref name="key"/>. The pre-refresh
    /// <c>item</c> reference becomes stale after <see cref="RefreshAsync"/>;
    /// looking up by key reaches the rebuilt instance.</summary>
    private void ClearDeactivatingState(
        (PimResourceKind Kind, string ResourceId, string ScopeId, string ObjectId, string TenantId) key)
    {
        foreach (var row in ActiveAssignments)
        {
            if (PendingMatchKey(row.Account, row.Assignment.Kind, row.Assignment.ResourceId, row.Assignment.ScopeId).Equals(key))
            {
                row.IsDeactivating = false;
            }
        }
    }

    /// <summary>Sets the inline error caption (or clears it when
    /// <paramref name="errorText"/> is null) on every row matching
    /// <paramref name="key"/>. Key-based lookup mirrors
    /// <see cref="ClearDeactivatingState"/> so a mid-flight refresh that
    /// rebuilt the row still gets the error painted onto the new
    /// instance.</summary>
    private void SetDeactivationErrorText(
        (PimResourceKind Kind, string ResourceId, string ScopeId, string ObjectId, string TenantId) key,
        string? errorText)
    {
        foreach (var row in ActiveAssignments)
        {
            if (PendingMatchKey(row.Account, row.Assignment.Kind, row.Assignment.ResourceId, row.Assignment.ScopeId).Equals(key))
            {
                row.DeactivationErrorText = errorText;
            }
        }
    }

    private async void OnActivationPanelClosed(ActivationResult? result)
    {
        if (result is null || _activationPanel.Eligibility is not { } eligibility)
        {
            return;
        }

        _toastService.ShowActivationResult(eligibility.DisplayName, result);

        // Graph PIM's read API is eventually consistent — show a placeholder
        // immediately, the next refresh swaps it for the real assignment
        // (or the 30 s watchdog drops it).
        if (ShouldShowPendingFor(result) && _activationPanel.Account is { } account)
        {
            var duration = result.EndDateTime is { } end
                ? end - DateTimeOffset.UtcNow
                : TimeSpan.FromHours(_activationPanel.DurationHours);
            var pending = ActiveAssignmentItemViewModel.CreatePending(
                eligibility,
                account,
                duration,
                DeactivateAsync);
            _tenantNameCache.TryGetValue(account.TenantId, out var cachedName);
            pending.TenantName = cachedName;
            ActiveAssignments.Insert(0, pending);
            UpdateActiveCount();
            MarkActiveEligibilities();
        }

        await RefreshAsync();

        if (ShouldShowPendingFor(result))
        {
            await Task.Delay(TimeSpan.FromSeconds(6));
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Pending placeholders only make sense when an activation is "fire-and-forget"
    /// from the user's perspective — they don't apply when an approver still has
    /// to sign off (no role appears, so no row should claim otherwise).
    /// </summary>
    private bool ShouldShowPendingFor(ActivationResult result)
        => result.IsSuccess && result.Status != ActivationStatus.PendingApproval;

    /// <summary>
    /// Fires after the add-tenant slide-in finishes. On success we drop the new
    /// enrollment into the accounts list, focus it (so its group is expanded
    /// after the refresh), and refresh so its eligibilities + active assignments
    /// show up immediately.
    /// </summary>
    private async void OnAddTenantPanelClosed(SignedInAccount? added)
    {
        if (added is null)
        {
            return;
        }

        EnrollAccountItem(added);
        ActiveAccount = added;
        IsSignedIn = true;
        await RefreshAsync();

        // Refresh rebuilt the groups; expand the new one (OnActiveAccountChanged
        // ran before the group existed so its expand call was a no-op).
        ExpandGroupFor(added);
    }

    private bool IsSameEnrollment(SignedInAccount? left, SignedInAccount? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(left.ObjectId, right.ObjectId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.TenantId, right.TenantId, StringComparison.OrdinalIgnoreCase)
            && left.Cloud == right.Cloud;
    }

    private string EnrollmentKey(SignedInAccount account)
        => $"{account.ObjectId}|{account.TenantId}|{account.Cloud}";

    private void UpdateCountdowns()
    {
        var settings = _userSettings.Current;
        var toastThreshold = TimeSpan.FromMinutes(settings.ExpiryWarningMinutes);

        foreach (var item in ActiveAssignments)
        {
            item.UpdateCountdown();

            if (!settings.ExpiryWarningEnabled || item.ExpiryWarningSent)
            {
                continue;
            }

            var remaining = item.RemainingTime;
            if (remaining > TimeSpan.Zero && remaining <= toastThreshold)
            {
                item.ExpiryWarningSent = true;
                _toastService.ShowExpiringSoon($"{item.AccountLabel} · {item.DisplayName}");
            }
        }
    }

    private void ReplaceAccounts(IReadOnlyList<SignedInAccount> accounts)
    {
        Accounts.Clear();
        foreach (var account in accounts)
        {
            EnrollAccountItem(account, makeActive: false);
        }

        ActiveAccount = Accounts.FirstOrDefault()?.Account;
    }

    /// <summary>
    /// Wraps <paramref name="account"/> in an <see cref="AccountListItemViewModel"/>,
    /// replaces any existing item with the same (oid, tenantId) pair, applies a
    /// cached tenant name if we have one, and kicks off a background fetch when
    /// we don't.
    /// </summary>
    private void EnrollAccountItem(SignedInAccount account, bool makeActive = false)
    {
        var existing = Accounts.FirstOrDefault(a => IsSameEnrollment(a.Account, account));
        if (existing is not null)
        {
            Accounts.Remove(existing);
        }

        var item = new AccountListItemViewModel(account);
        if (_tenantNameCache.TryGetValue(account.TenantId, out var cachedName))
        {
            item.TenantName = cachedName;
        }
        else
        {
            _ = LoadTenantNameForItemAsync(item);
        }

        Accounts.Add(item);

        if (makeActive)
        {
            ActiveAccount = account;
        }
    }

    /// <summary>
    /// Fetches the tenant display name for a single account list row and, on
    /// success, populates the shared cache plus every row that displays a
    /// label for the same tenant id.
    /// </summary>
    private async Task LoadTenantNameForItemAsync(AccountListItemViewModel item)
    {
        try
        {
            var name = await _tenantInfoService.GetTenantDisplayNameAsync(item.Account);
            _tenantNameCache[item.Account.TenantId] = name;
            item.TenantName = name;

            PushTenantNameToBoundRows(item.Account.TenantId, name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to fetch tenant name for enrollment (oid {ObjectId}, tenant {TenantId})",
                item.Account.ObjectId,
                item.Account.TenantId);
        }
    }

    private void PushTenantNameToBoundRows(string tenantId, string? name)
    {
        foreach (var row in ActiveAssignments)
        {
            if (string.Equals(row.Account.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            {
                row.TenantName = name;
            }
        }

        foreach (var group in EligibilityGroups)
        {
            if (!string.Equals(group.Account.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            group.TenantName = name;
            foreach (var row in group.Items)
            {
                row.TenantName = name;
            }
        }
    }

    /// <summary>
    /// Rebuilds <see cref="EligibilityGroups"/> from the aggregated dict.
    /// Preserves <see cref="TenantEligibilityGroup.IsExpanded"/> across
    /// rebuilds so a refresh doesn't snap the user's open/closed layout shut.
    /// </summary>
    private void BuildEligibilityGroups(
        IReadOnlyDictionary<SignedInAccount, IReadOnlyList<PimEligibility>> aggregated)
    {
        // Snapshot prior expansion keyed by (oid, tid) so we can carry it
        // forward — refresh rebuilds the group instances.
        var previousExpansion = EligibilityGroups
            .ToDictionary(g => EnrollmentKey(g.Account), g => g.IsExpanded);

        EligibilityGroups.Clear();

        // Order groups by the Accounts collection order (which itself is the
        // enrollment order) so the layout is stable across refreshes.
        foreach (var accountItem in Accounts)
        {
            var account = accountItem.Account;
            if (!aggregated.TryGetValue(account, out var rows))
            {
                rows = Array.Empty<PimEligibility>();
            }

            _tenantNameCache.TryGetValue(account.TenantId, out var cachedName);

            var group = new TenantEligibilityGroup(account)
            {
                SuppressUserExpansionEvent = true,
                TenantName = cachedName,
            };
            foreach (var eligibility in rows)
            {
                group.Items.Add(new EligibilityItemViewModel(eligibility, account, ActivateAsync)
                {
                    TenantName = cachedName,
                });
            }

            // Default-expand layering (highest priority first):
            //   1. previous in-memory state (carried across this refresh)
            //   2. persisted ExpandedTenants[tid] from UserSettings
            //   3. fallback: the active account's group is open, others closed.
            var key = EnrollmentKey(account);
            if (previousExpansion.TryGetValue(key, out var prior))
            {
                group.IsExpanded = prior;
            }
            else if (_userSettings.Current.ExpandedTenants is { } savedDict
                && savedDict.TryGetValue(account.TenantId, out var savedExpanded))
            {
                group.IsExpanded = savedExpanded;
            }
            else
            {
                group.IsExpanded = IsSameEnrollment(account, ActiveAccount);
            }

            group.SuppressUserExpansionEvent = false;
            group.ExpansionToggledByUser += value => OnGroupExpansionToggledByUser(group, value);

            EligibilityGroups.Add(group);
        }

        UpdateEligibleCount();
    }

    /// <summary>
    /// Re-applies the current <see cref="FilterText"/> to all groups. Updates
    /// <see cref="TenantEligibilityGroup.MatchCount"/> and
    /// <see cref="TenantEligibilityGroup.IsVisible"/>. Snapshots / restores
    /// per-group expansion state across the filter-active boundary so the
    /// auto-expand-on-match behaviour doesn't poison the user's layout when
    /// they clear the filter.
    /// </summary>
    private void ApplyFilter()
    {
        var filter = FilterText?.Trim() ?? string.Empty;
        var filterActive = filter.Length > 0;

        // Entering filter mode → snapshot. Leaving filter mode → restore.
        if (filterActive && _preFilterExpansion is null)
        {
            _preFilterExpansion = EligibilityGroups.ToDictionary(
                g => EnrollmentKey(g.Account),
                g => g.IsExpanded);
        }
        else if (!filterActive && _preFilterExpansion is { } snapshot)
        {
            foreach (var group in EligibilityGroups)
            {
                group.SuppressUserExpansionEvent = true;
                try
                {
                    group.IsExpanded = snapshot.TryGetValue(EnrollmentKey(group.Account), out var prior)
                        ? prior
                        : IsSameEnrollment(group.Account, ActiveAccount);
                }
                finally
                {
                    group.SuppressUserExpansionEvent = false;
                }
            }

            _preFilterExpansion = null;
        }

        foreach (var group in EligibilityGroups)
        {
            var matches = filterActive
                ? group.Items.Count(item => item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                : group.Items.Count;

            group.MatchCount = matches;
            group.IsVisible = filterActive ? matches > 0 : true;

            if (filterActive)
            {
                // Auto-expand groups with matches — without this, hits stay
                // hidden behind a collapsed header and the search feels broken.
                group.SuppressUserExpansionEvent = true;
                try
                {
                    group.IsExpanded = matches > 0;
                }
                finally
                {
                    group.SuppressUserExpansionEvent = false;
                }
            }
        }

        UpdateEligibleCount();
    }

    /// <summary>
    /// Called when the user clicks a group header to toggle expansion.
    /// Persists the per-tenant choice and keeps the pre-filter snapshot in
    /// sync so a clear-filter restore reflects the user's most recent intent.
    /// </summary>
    private void OnGroupExpansionToggledByUser(TenantEligibilityGroup group, bool expanded)
    {
        if (_preFilterExpansion is { } snapshot)
        {
            snapshot[EnrollmentKey(group.Account)] = expanded;
        }

        PersistShellSettings(s =>
        {
            var dict = s.ExpandedTenants is null
                ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>(s.ExpandedTenants, StringComparer.OrdinalIgnoreCase);
            dict[group.Account.TenantId] = expanded;
            return s with { ExpandedTenants = dict };
        });
    }

    /// <summary>
    /// Applies <paramref name="transform"/> to <see cref="IUserSettingsService.Current"/>
    /// and persists the result. Fire-and-forget — IO errors are logged but
    /// don't propagate so a transient write failure can't crash the shell.
    /// </summary>
    private void PersistShellSettings(Func<UserSettings, UserSettings> transform)
    {
        var updated = transform(_userSettings.Current);
        _ = SafePersistAsync(updated);
    }

    private async Task SafePersistAsync(UserSettings settings)
    {
        try
        {
            await _userSettings.SaveAsync(settings).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist shell-layout user settings");
        }
    }

    private void ExpandGroupFor(SignedInAccount account)
    {
        var group = EligibilityGroups.FirstOrDefault(g => IsSameEnrollment(g.Account, account));
        if (group is null)
        {
            return;
        }

        group.SuppressUserExpansionEvent = true;
        try
        {
            group.IsExpanded = true;
        }
        finally
        {
            group.SuppressUserExpansionEvent = false;
        }

        // Keep the pre-filter snapshot in sync so the restore on filter-clear
        // doesn't snap the freshly-focused group shut again.
        if (_preFilterExpansion is { } snapshot)
        {
            snapshot[EnrollmentKey(account)] = true;
        }
    }

    private void UpdateActiveAssignments(
        IReadOnlyDictionary<SignedInAccount, IReadOnlyList<ActiveAssignment>> aggregated)
    {
        var existingPendings = ActiveAssignments.Where(a => a.IsPending).ToList();
        var staleThreshold = DateTimeOffset.UtcNow - PendingWatchdog;

        var deactivatingKeys = ActiveAssignments
            .Where(a => a.IsDeactivating)
            .Select(a => PendingMatchKey(a.Account, a.Assignment.Kind, a.Assignment.ResourceId, a.Assignment.ScopeId))
            .ToHashSet();

        // Carry inline deactivation-error captions across the rebuild so a
        // refresh that fires between the failed POST and the user's eyes
        // doesn't wipe the error signal off the row.
        var errorTexts = ActiveAssignments
            .Where(a => !string.IsNullOrEmpty(a.DeactivationErrorText))
            .ToDictionary(
                a => PendingMatchKey(a.Account, a.Assignment.Kind, a.Assignment.ResourceId, a.Assignment.ScopeId),
                a => a.DeactivationErrorText);

        ActiveAssignments.Clear();

        var addedKeys = new HashSet<(PimResourceKind, string, string, string, string)>();
        foreach (var (account, list) in aggregated)
        {
            _tenantNameCache.TryGetValue(account.TenantId, out var cachedName);

            foreach (var assignment in list)
            {
                var key = PendingMatchKey(account, assignment.Kind, assignment.ResourceId, assignment.ScopeId);
                errorTexts.TryGetValue(key, out var carriedError);
                ActiveAssignments.Add(new ActiveAssignmentItemViewModel(assignment, account, DeactivateAsync)
                {
                    TenantName = cachedName,
                    IsDeactivating = deactivatingKeys.Contains(key),
                    DeactivationErrorText = carriedError,
                });
                addedKeys.Add(key);
            }
        }

        foreach (var pending in existingPendings)
        {
            var key = PendingMatchKey(
                pending.Account,
                pending.Assignment.Kind,
                pending.Assignment.ResourceId,
                pending.Assignment.ScopeId);
            if (addedKeys.Contains(key))
            {
                continue;
            }

            if (pending.CreatedAt < staleThreshold)
            {
                _logger.LogWarning(
                    "Pending activation watchdog dropped row {Role} for tenant {TenantId} — no real assignment after {Timeout}",
                    pending.DisplayName,
                    pending.Account.TenantId,
                    PendingWatchdog);
                continue;
            }

            ActiveAssignments.Insert(0, pending);
        }

        UpdateActiveCount();
    }

    private (PimResourceKind Kind, string ResourceId, string ScopeId, string ObjectId, string TenantId)
        PendingMatchKey(SignedInAccount account, PimResourceKind kind, string resourceId, string scopeId)
        => (kind, resourceId, scopeId, account.ObjectId, account.TenantId);

    /// <summary>
    /// Flags any eligibility row whose (Kind, ResourceId, ScopeId, oid, tid)
    /// tuple matches an active assignment so the row can be dimmed and made
    /// non-clickable. Composite key includes account so the same identity in
    /// two tenants never cross-poisons rows.
    /// </summary>
    private void MarkActiveEligibilities()
    {
        var activeKeys = new HashSet<(PimResourceKind, string, string, string, string)>(
            ActiveAssignments.Select(row => (
                row.Assignment.Kind,
                row.Assignment.ResourceId,
                row.Assignment.ScopeId,
                row.Account.ObjectId,
                row.Account.TenantId)));

        foreach (var group in EligibilityGroups)
        {
            foreach (var item in group.Items)
            {
                var key = (
                    item.Eligibility.Kind,
                    item.Eligibility.ResourceId,
                    item.Eligibility.ScopeId,
                    item.Account.ObjectId,
                    item.Account.TenantId);
                item.IsCurrentlyActive = activeKeys.Contains(key);
            }
        }
    }

    private void UpdateActiveCount()
    {
        var newCount = ActiveAssignments.Count;
        if (newCount == ActiveCount)
        {
            return;
        }

        ActiveCount = newCount;
        ActiveCountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateEligibleCount()
    {
        EligibleCount = EligibilityGroups.Sum(g => g.MatchCount);
    }
}
