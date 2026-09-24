namespace EntraPimManager.AppAvalonia.ViewModels;

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EntraPimManager.AppAvalonia.Services;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Collections;
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
    // Single source of truth for the project URL — used by the "View on GitHub"
    // command and the auto-updater's GitHub release source (see UpdateService).
    internal const string GitHubProjectUrl = "https://github.com/junisconsulting/entra-pim-manager";

    // Cap on parallel policy prefetch requests. Microsoft Graph PIM endpoints
    // have global throttling — 6 in flight at once is enough to feel snappy
    // without risking 429s.
    private const int PolicyPrefetchConcurrency = 6;

    /// <summary>
    /// Upper bound on eligibilities warmed per refresh. The prefetch is a
    /// convenience — it saves a 1-3 s wait on the first click — and there is one
    /// target per (role, scope), so an account eligible across a large Azure estate
    /// would otherwise fire several hundred ARM policy reads on every refresh. What
    /// is not prefetched still activates: ActivateAsync fetches lazily behind the
    /// spinner the row already shows.
    /// </summary>
    private const int PolicyPrefetchLimit = 100;

    // Recent activations: three are shown, more are remembered so an entry that is
    // temporarily pinned or unavailable can come back instead of being forgotten.
    private const int RecentShortcutLimit = 3;
    private const int RecentShortcutMemory = 10;

    /// <summary>
    /// Up to this many eligibilities, a tenant's sections open with it — headers over a
    /// handful of rows are noise. Above it they start collapsed, which is the whole point
    /// of having them.
    /// </summary>
    private const int SectionAutoExpandLimit = 12;

    /// <summary>
    /// Appended to the justification an extension carries over, so the audit trail
    /// says which entry was the original grant and which one bought more time.
    /// </summary>
    private const string ExtendJustificationSuffix = " - extend time";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan GraphCallTimeout = TimeSpan.FromSeconds(30);

    // Strictly longer than the aggregator's 30 s per-account budget. The two
    // timers start together, so an equal value lets this one fire first and
    // discard every tenant's result — the per-tenant isolation only works
    // while the inner budget is the one that expires.
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(45);

    // How long an "Activating…" placeholder may live without Graph publishing
    // the real assignment before we give up and drop it.
    private static readonly TimeSpan PendingWatchdog = TimeSpan.FromSeconds(30);

    // Microsoft's read API is eventually consistent: a deactivated assignment can
    // take 5-60 s to drop off the schedule-instances endpoint. Poll for that long
    // before declaring the deactivation unconfirmed.
    private static readonly TimeSpan DeactivationWatchdog = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DeactivationPollInterval = TimeSpan.FromSeconds(6);

    private readonly IAuthService _authService;
    private readonly IEligibilityAggregator _aggregator;
    private readonly IAccountScopedServices _accountServices;
    private readonly ITenantInfoService _tenantInfoService;
    private readonly IToastService _toastService;
    private readonly IUserSettingsService _userSettings;
    private readonly IScopeFavoritesStore _scopeFavorites;
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

    // Expiry-warning "already dismissed" set, keyed by assignment identity. Lives
    // here (not on the row VMs, which are rebuilt on every 60s refresh) so a
    // warning the user dismissed stays dismissed across refreshes and re-arms only
    // when that assignment leaves and re-enters the warning window.
    private readonly HashSet<string> _expiryDismissed = new(StringComparer.Ordinal);

    // Enrollments whose last active-assignments read failed. A failed read comes back
    // as an empty list, which on its own is indistinguishable from "nothing is active"
    // — and for anything waiting on a role to be given up, that difference is the
    // difference between "PIM let go" and "the network blinked".
    private readonly HashSet<string> _activeReadFailures = new(StringComparer.OrdinalIgnoreCase);

    // Subscription display names by subscription id, harvested from every row whose
    // scope IS a subscription. ARM does not return the subscription's name on a
    // resource-group-scoped assignment — only its id sits in the scope path — and
    // this is the answer without spending a read on it.
    private readonly Dictionary<string, string> _subscriptionNames = new(StringComparer.OrdinalIgnoreCase);

    // Justification of every activation this session granted, keyed by assignment
    // identity, so "Extend time" can offer the same reason again instead of making
    // the user retype it. In memory for the session only: justification text may
    // carry incident detail, so it is never written to disk and never logged.
    private readonly Dictionary<(PimResourceKind Kind, string ResourceId, string ScopeId, string ObjectId, string TenantId), string>
        _grantedJustifications = [];

    // Snapshot of per-group IsExpanded state taken at the moment a filter
    // becomes active, restored when the user clears the filter. Without this
    // the auto-expand-on-match behaviour would overwrite the layout the user
    // had before they started typing.
    private Dictionary<string, bool>? _preFilterExpansion;

    // Expansion state of the per-role Azure nodes, carried across refreshes only.
    private Dictionary<string, bool> _roleGroupExpansion = new(StringComparer.Ordinal);

    private Dictionary<string, bool> _sectionExpansion = new(StringComparer.Ordinal);

    private Dictionary<string, bool>? _preFilterSectionExpansion;

    // Pinned eligibility keys, mirrored from user settings so a toggle does not have
    // to wait for the write to land before the next rebuild reads it back.
    private HashSet<string> _pinnedKeys = new(StringComparer.Ordinal);

    // Cancellation handle for the background policy prefetch. Each new
    // RefreshAsync cancels the previous prefetch (which is likely working
    // on stale data anyway) before starting a fresh one.
    private CancellationTokenSource? _prefetchCts;

    // Identity of the assignment currently shown in the alert window, so a
    // Dismiss/Open click knows which key to suppress.
    private string? _currentAlertKey;

    // The row behind that alert, for its Re-activate button.
    private ActiveAssignmentItemViewModel? _currentAlertRow;

    // Last surfaced "expiring" signature, so ExpiringChanged only fires when the
    // tray-visible state actually changes instead of on every 1s tick.
    private string _lastExpiringSignature = string.Empty;

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

    /// <summary>
    /// True while the standalone expiry-alert window should be on screen — i.e. an
    /// active assignment is inside the warning window and hasn't been dismissed.
    /// <see cref="Tray.ExpiryAlertController"/> observes this to show / hide.
    /// </summary>
    [ObservableProperty]
    private bool _isExpiryAlertVisible;

    /// <summary>
    /// True while at least one pinned or recent row exists. Hidden during a search:
    /// with the same hit above and below, the shortcuts turn into noise.
    /// </summary>
    [ObservableProperty]
    private bool _hasShortcuts;

    public ShellViewModel(
        IAuthService authService,
        IEligibilityAggregator aggregator,
        IAccountScopedServices accountServices,
        ITenantInfoService tenantInfoService,
        IToastService toastService,
        IUserSettingsService userSettings,
        IScopeFavoritesStore scopeFavorites,
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
        _scopeFavorites = scopeFavorites;
        _options = options.Value;
        _activationPanel = activationPanel;
        _addTenantPanel = addTenantPanel;
        _settingsPanel = settingsPanel;
        _logger = logger;

        // A label the admin gave a tenant in Settings beats the directory's
        // display name everywhere a tenant is named. Seeding the cache is
        // enough: every row reads it first and only asks Graph on a miss.
        foreach (var registration in _options.TenantAppRegistrations)
        {
            if (!string.IsNullOrWhiteSpace(registration.Label) && Guid.TryParse(registration.TenantId, out var tenant))
            {
                _tenantNameCache[tenant.ToString()] = registration.Label.Trim();
            }
        }

        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => UpdateCountdowns();

        _activationPanel.Activated += OnActivated;

        // Re-activation ends the running activation from inside the panel's submit;
        // deactivation and the refresh that confirms it live here.
        _activationPanel.EndRunningActivation = EndRunningActivationAsync;

        // Scope favourites are written from the activation panel; the starred ones are
        // rows on the main page, so they follow the store. Changed fires on the writing
        // thread, hence the dispatch.
        _scopeFavorites.Changed += () => Dispatcher.UIThread.Post(RebuildShortcutSections);
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
    /// Raised when the "expiring soon" tray state changes — the soonest expiring
    /// assignment, its remaining-time label, or the count. The tray controller
    /// listens to refresh its icon + tooltip without polling every tick.
    /// </summary>
    public event EventHandler? ExpiringChanged;

    /// <summary>
    /// Live model bound by the standalone expiry-alert window. Mutated in place
    /// every countdown tick so the surfaced countdown ticks without rebinding.
    /// </summary>
    public ExpiryAlertViewModel ExpiryAlert { get; } = new();

    /// <summary>
    /// Soonest active assignment currently inside the warning window, regardless
    /// of dismissal (the tray keeps reflecting it even after the alert is
    /// dismissed). <c>null</c> when nothing is expiring.
    /// </summary>
    public ActiveAssignmentItemViewModel? MostUrgentExpiring { get; private set; }

    /// <summary>How many active assignments are inside the warning window right now.</summary>
    public int ExpiringCount { get; private set; }

    /// <summary>
    /// Enrolled accounts in stable order, wrapped so the row can carry a
    /// mutable tenant label. Grouped by tenant in the Settings TENANTS section.
    /// </summary>
    public ObservableCollection<AccountListItemViewModel> Accounts { get; } = [];

    /// <summary>Eligibilities grouped per enrolled account / tenant.</summary>
    public ObservableCollection<TenantEligibilityGroup> EligibilityGroups { get; } = [];

    /// <summary>Cross-tenant: active assignments across every enrolled account.</summary>
    public ObservableCollection<ActiveAssignmentItemViewModel> ActiveAssignments { get; } = [];

    /// <summary>Eligibilities the user pinned, shown above the tenant groups.</summary>
    public ObservableCollection<EligibilityItemViewModel> PinnedItems { get; } = [];

    /// <summary>The last few activations, shown under the pinned ones.</summary>
    public ObservableCollection<EligibilityItemViewModel> RecentItems { get; } = [];

    /// <summary>The slide-in activation panel — bound by the popup window.</summary>
    public ActivationPanelViewModel ActivationPanel => _activationPanel;

    /// <summary>The slide-in "connect to additional tenant" panel — bound by the popup window.</summary>
    public AddTenantPanelViewModel AddTenantPanel => _addTenantPanel;

    /// <summary>The slide-in Settings panel — bound by the popup window.</summary>
    public SettingsPanelViewModel SettingsPanel => _settingsPanel;

    /// <summary>
    /// True when not a single cloud has a usable App Registration client id —
    /// every entry is empty or a non-GUID placeholder. The main popup shows a
    /// first-run empty state pointing the user at Settings → Tenants;
    /// the regular eligibility / active lists stay hidden until this is false.
    /// </summary>
    public bool NeedsConfiguration => _options.ConfiguredClouds().Count == 0;

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
            // popup will guide the user to Settings → Tenants.
            _countdownTimer.Start();
            return;
        }

        try
        {
            var accounts = await _authService.GetAllAccountsAsync(ct);
            ReplaceAccounts(accounts);

            // Installations that enrolled their accounts before VerifiedClientIds
            // existed have no record to compare against. Those enrollments were
            // still proven by a real sign-in — against the client ids configured
            // then, which for an upgrade are necessarily the current ones. Adopt
            // them once so the user isn't told to verify an already-working setup.
            if (_userSettings.Current.VerifiedClientIds is null && Accounts.Count > 0)
            {
                MarkAppRegistrationVerified(Accounts.Select(a => a.Account));
            }

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
    /// Reorders the tenant cards so <paramref name="dragged"/> lands where
    /// <paramref name="target"/> was, and derives the flat account order from the new
    /// tenant order. Used by the DnD wiring in the Settings TENANTS section.
    /// </summary>
    /// <remarks>
    /// The persisted model is still a flat list of accounts, and the popup takes its
    /// group order from it — so the tenant order has to be written back into that list.
    /// Rewriting it is only ever done here, on an explicit drop: doing it while merely
    /// rendering Settings would silently reshuffle a popup the user never touched.
    /// Accounts keep their order within their tenant.
    /// </remarks>
    /// <param name="dragged">The tenant card being moved.</param>
    /// <param name="target">The card it was dropped on.</param>
    public async Task MoveTenantAsync(TenantNodeViewModel dragged, TenantNodeViewModel target)
    {
        if (dragged is null || target is null)
        {
            return;
        }

        var nodes = _settingsPanel.Nodes.ToList();
        var from = nodes.IndexOf(dragged);
        var to = nodes.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
        {
            return;
        }

        nodes.RemoveAt(from);
        nodes.Insert(to, dragged);

        var ordered = nodes.SelectMany(n => n.Accounts).ToList();
        if (ordered.Count != Accounts.Count)
        {
            // Every account belongs to exactly one card, so this cannot happen — but a
            // partial order written to disk would be worse than an ignored drop.
            _logger.LogWarning(
                "Tenant reorder produced {Ordered} of {Total} accounts; ignoring the drop",
                ordered.Count,
                Accounts.Count);
            return;
        }

        ObservableCollectionSync.Apply(Accounts, ordered);
        RebuildGroupsFromAccountOrder();

        try
        {
            await _authService.ReorderAccountsAsync(
                Accounts.Select(a => a.Account).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist tenant reorder");
        }
    }

    /// <summary>
    /// Marks the assignment currently shown in the alert as dismissed and hides
    /// the window. Called from the alert's "Dismiss" / "Open" buttons via
    /// <see cref="Tray.ExpiryAlertController"/>.
    /// </summary>
    public void DismissCurrentExpiryAlert()
    {
        if (_currentAlertKey is { } key)
        {
            _expiryDismissed.Add(key);
        }

        IsExpiryAlertVisible = false;
    }

    /// <summary>
    /// Re-activates the assignment the alert is currently showing. Called from the
    /// alert's "Re-activate" button via <see cref="Tray.ExpiryAlertController"/>,
    /// which surfaces the popup first — that is where the activation panel lives.
    /// </summary>
    public Task ReactivateCurrentExpiryAlertAsync()
        => _currentAlertRow is { } row ? ReactivateAsync(row) : Task.CompletedTask;

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
            using var cts = new CancellationTokenSource(RefreshTimeout);
            var snapshot = Accounts.Select(a => a.Account).ToList();

            // Both reads fan out across all enrolled accounts in parallel with
            // per-account timeouts — a slow or broken tenant cannot block the
            // rest.
            var activeTask = _aggregator.GetAggregatedActiveAssignmentsAsync(snapshot, cts.Token);
            var eligibilityTask = _aggregator.GetAggregatedEligibilitiesAsync(snapshot, cts.Token);
            await Task.WhenAll(activeTask, eligibilityTask);

            HarvestSubscriptionNames(activeTask.Result, eligibilityTask.Result);

            _activeReadFailures.Clear();
            foreach (var (account, fetched) in activeTask.Result)
            {
                if (fetched.LoadError is not null)
                {
                    _activeReadFailures.Add(EnrollmentKey(account));
                }
            }

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

            // Signing in again after the Azure consent was finally granted is the
            // obvious remedy, and the backoff key would otherwise survive it.
            _aggregator.ForgetAzureBackoff(account);

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

            // The shortcut sections are derived from EligibilityGroups, so they only
            // drop the removed enrollment's rows when something rebuilds them — and
            // the refresh that normally does is stopped below once the last account
            // is gone. Without this a pinned row outlives its account, sits next to
            // the "no accounts" empty state, and routes a click at an enrollment
            // that no longer exists.
            RebuildShortcutSections();

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

    /// <summary>Opens the "Add account" slide-in for <paramref name="slot"/> (broker
    /// sign-in primary, device code under Advanced). Closes Settings first: the slide-in
    /// panels are overlapping siblings and Settings renders on top, so otherwise this
    /// panel opens invisibly behind it.</summary>
    /// <param name="slot">The tenant to sign in to, from the card the button sits in.</param>
    [RelayCommand]
    private void OpenAddAccountPanel(TenantSlot? slot)
    {
        _settingsPanel.IsOpen = false;
        if (slot is null)
        {
            _addTenantPanel.Open();
            return;
        }

        _addTenantPanel.Open(slot.Cloud, slot.TenantId);
    }

    /// <summary>Opens the Settings slide-in. Closes any other open slide-in first
    /// so the panel slots stay mutually exclusive. Sets <c>IsOpen=false</c>
    /// directly on the other panels, which fires no <c>Closed</c> and therefore
    /// re-runs no activation / tenant-add side effects. An activation batch in
    /// flight keeps its panel: a per-scope failure has nowhere else to be read, and
    /// the panel's own Back button is gated the same way.</summary>
    [RelayCommand]
    private void OpenSettingsPanel()
    {
        if (!_activationPanel.IsBusy)
        {
            _activationPanel.IsOpen = false;
        }

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

        // Pinned first: past the limit, the rows someone actually clicks are the ones
        // they pinned. OrderByDescending is stable, so the rest keep list order.
        var targets = EligibilityGroups
            .SelectMany(g => g.Items.Select(i => (g.Account, i.Eligibility)))
            .OrderByDescending(t => _pinnedKeys.Contains(ShortcutKey(t.Account, t.Eligibility)))
            .Take(PolicyPrefetchLimit)
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
                        target.Eligibility.ScopeId,
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
    private void OpenGitHub() => OpenInBrowser(GitHubProjectUrl, "GitHub project page");

    /// <summary>
    /// Opens the GitHub release this build came from — the footer version is the
    /// link, so "what changed in the version I am running" is one click away.
    /// </summary>
    [RelayCommand]
    private void OpenReleaseNotes()
    {
        // A build that never became a release has no tag to point at: the verify
        // packages carry a "-local.<timestamp>" suffix and a plain `dotnet run`
        // reports 1.0.0. Those get the releases list rather than a certain 404.
        // The informational version is what carries the suffix; the assembly
        // version behind VersionText has already dropped it.
        var informational = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var isTagged = informational is not null && !informational.Split('+')[0].Contains('-');

        // VersionText already carries the "v" prefix the tags use.
        var url = isTagged
            ? $"{GitHubProjectUrl}/releases/tag/{VersionText}"
            : $"{GitHubProjectUrl}/releases";
        OpenInBrowser(url, "release page");
    }

    /// <summary>
    /// Hands a URL to the user's default browser. Shared so the footer's two links
    /// cannot drift apart in how they fail — neither is worth an error in the UI.
    /// </summary>
    private void OpenInBrowser(string url, string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open the {Target}", target);
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
        item.ActivationErrorText = null;
        try
        {
            using var cts = new CancellationTokenSource(GraphCallTimeout);
            var bundle = _accountServices.GetServicesFor(account);
            var policy = await bundle.PolicyService.GetPolicyAsync(
                account.TenantId,
                item.Eligibility.Kind,
                item.Eligibility.ResourceId,
                item.Eligibility.ScopeId,
                cts.Token);
            _settingsPanel.IsOpen = false;

            // Switch the "default" account to the one the user is acting on
            // so the Phase 4 LastUsedAccount persistence captures intent.
            if (!IsSameEnrollment(ActiveAccount, account))
            {
                ActiveAccount = account;
            }

            _activationPanel.Open(account, item.Eligibility, policy, item.Favorite);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading policy failed");
            var mapped = PimErrorMapper.MapException(ex).Message;
            _toastService.ShowError("Activation", mapped);
            item.ActivationErrorText = mapped;
        }
        finally
        {
            item.IsActivating = false;
        }
    }

    /// <summary>
    /// Re-activate an active row: open the activation panel in replace mode, so the
    /// running activation is ended and immediately requested again.
    /// </summary>
    /// <remarks>
    /// PIM has no way to extend a running activation — a second request for a role
    /// that is still active comes back as <c>RoleAssignmentExists</c>, whatever start
    /// time it carries. Giving the role up and asking again is the only path, which
    /// is why this goes through the panel rather than acting on one click: the user
    /// sees what it costs, and fills in the justification the new request needs.
    /// </remarks>
    private async Task ReactivateAsync(ActiveAssignmentItemViewModel item)
    {
        var account = item.Account;

        // A release plus activation is already in flight. A second one would fire
        // another deactivation for the same assignment behind the first one's back,
        // and re-opening the panel would orphan the batch that is running.
        if (_activationPanel.IsBusy)
        {
            return;
        }

        // The alert window reaches this without the row button's guard, and an
        // activation can be both younger than five minutes and close to expiring.
        // Ending it would be refused, so say so before the user fills in a form.
        if (item.IsInProvisioningWindow)
        {
            _toastService.ShowError("Can't extend yet", item.ReactivateTooltip);
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(GraphCallTimeout);
            var bundle = _accountServices.GetServicesFor(account);
            var policy = await bundle.PolicyService.GetPolicyAsync(
                account.TenantId,
                item.Assignment.Kind,
                item.Assignment.ResourceId,
                item.Assignment.ScopeId,
                cts.Token);

            // An approval-gated role has no re-activation worth offering: the running
            // activation would end now and the replacement would sit in PendingApproval,
            // leaving the user with nothing in between. Say so instead of doing it.
            if (policy.RequiresApproval)
            {
                _toastService.ShowError(
                    "Can't extend this role",
                    $"{item.DisplayName} requires approval. Extending means giving the role up first, which would leave you waiting for an approver with no access. Let it expire and request again — and tell your approver beforehand.");
                return;
            }

            if (FindEligibilityFor(item) is not { } eligibility)
            {
                _toastService.ShowError(
                    "Can't extend this role",
                    $"No eligibility for {item.DisplayName} is listed any more. Refreshing…");
                await RefreshAsync();
                return;
            }

            _settingsPanel.IsOpen = false;
            if (!IsSameEnrollment(ActiveAccount, account))
            {
                ActiveAccount = account;
            }

            _activationPanel.Open(
                account,
                eligibility,
                policy,
                preselect: ScopePreselectionFor(account, eligibility, item.Assignment),
                replacing: item.Assignment,
                justification: CarriedJustificationFor(item));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading policy for re-activation failed");
            _toastService.ShowError("Extend time", PimErrorMapper.MapException(ex).Message);
        }
    }

    /// <summary>
    /// The eligibility the panel needs to request <paramref name="item"/> again.
    /// </summary>
    /// <remarks>
    /// Usually the row with the very same key. An Azure activation narrowed to a
    /// subscription is the exception: it lists at that subscription while its
    /// eligibility stays on the management group above, so the exact key finds
    /// nothing and the role alone has to do. The panel then opens with an empty
    /// scope picker and the user names the scope again — which is the existing rule
    /// that a narrowed activation is chosen per activation, not inherited.
    /// </remarks>
    private PimEligibility? FindEligibilityFor(ActiveAssignmentItemViewModel item)
    {
        var rows = EligibilityGroups.SelectMany(group => group.Items).ToList();
        var wanted = PendingMatchKey(
            item.Account,
            item.Assignment.Kind,
            item.Assignment.ResourceId,
            item.Assignment.ScopeId);

        var exact = rows.FirstOrDefault(row => PendingMatchKey(
            row.Account,
            row.Eligibility.Kind,
            row.Eligibility.ResourceId,
            row.Eligibility.ScopeId).Equals(wanted));
        if (exact is not null)
        {
            return exact.Eligibility;
        }

        // Only Azure roles fall back. A directory role at another scope is another
        // grant under the same name: ending an administrative-unit-scoped activation
        // and re-requesting the tenant-wide eligibility would hand back more than was
        // given up, which is the one mistake this whole feature must not make.
        if (item.Assignment.Kind != PimResourceKind.AzureResourceRole)
        {
            return null;
        }

        var candidates = rows
            .Where(row =>
                IsSameEnrollment(row.Account, item.Account)
                && row.Eligibility.Kind == PimResourceKind.AzureResourceRole
                && string.Equals(
                    RoleDefinitionKey(row.Eligibility.ResourceId),
                    RoleDefinitionKey(item.Assignment.ResourceId),
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Whether a subscription sits beneath a given management group is not derivable
        // from the scope ids — it takes the eligibleChildResources walk the panel does
        // later. So one candidate is an answer and several are a guess, and a guess here
        // activates the role somewhere the user did not ask for.
        return candidates.Count == 1 ? candidates[0].Eligibility : null;
    }

    /// <summary>
    /// Remembers the display name of every scope that is a subscription, from both
    /// reads, so a resource-group-scoped row can name the subscription it sits in.
    /// Never cleared: a subscription that drops out of one refresh has not been
    /// renamed, and a remembered name beats falling back to its GUID.
    /// </summary>
    private void HarvestSubscriptionNames(
        IReadOnlyDictionary<SignedInAccount, ActiveAssignmentFetchResult> active,
        IReadOnlyDictionary<SignedInAccount, EligibilityFetchResult> eligibilities)
    {
        var scopes = active
            .SelectMany(entry => entry.Value.Items)
            .Select(assignment => (assignment.ScopeId, assignment.ScopeLabel))
            .Concat(eligibilities
                .SelectMany(entry => entry.Value.Items)
                .Select(eligibility => (eligibility.ScopeId, eligibility.ScopeLabel)));

        foreach (var (scopeId, scopeLabel) in scopes)
        {
            if (ScopeNameFormatter.SubscriptionScopeOf(scopeId) is { } subscriptionId)
            {
                _subscriptionNames[subscriptionId] = ScopeNameFormatter.NameOf(scopeLabel, scopeId);
            }
        }
    }

    /// <summary>
    /// The subscription name to hand a row, or <c>null</c> when the scope is not
    /// under a subscription we have a name for.
    /// </summary>
    private string? SubscriptionNameFor(string scopeId)
        => ScopeNameFormatter.SubscriptionIdOf(scopeId) is { } subscriptionId
            && _subscriptionNames.TryGetValue(subscriptionId, out var name)
                ? name
                : null;

    /// <summary>
    /// Ticks the scope the expiring activation is actually on, so extending an Azure
    /// role does not start at an empty "Activate on" picker.
    /// </summary>
    /// <remarks>
    /// Only for an eligibility that can narrow, and only when the activation sits
    /// somewhere other than the eligibility's own scope — otherwise there is no
    /// picker, or nothing to choose. It travels as an unsaved
    /// <see cref="ScopeFavorite"/> purely to reuse the panel's existing
    /// preselect-once-the-list-loads machinery, including its refusal to fall back to
    /// the entire scope when the scope cannot be found. Nothing persists it.
    /// </remarks>
    private ScopeFavorite? ScopePreselectionFor(
        SignedInAccount account,
        PimEligibility eligibility,
        ActiveAssignment assignment)
    {
        if (!eligibility.CanNarrowScope
            || string.Equals(eligibility.ScopeId, assignment.ScopeId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // The picker matches on the scope id alone; the name and type only ever reach
        // the "no longer available" notice, so they come off the row's own label.
        var label = assignment.ScopeLabel;
        var separator = label?.IndexOf(": ", StringComparison.Ordinal) ?? -1;
        var name = separator >= 0 ? label![(separator + 2)..] : label ?? assignment.ScopeId;
        var type = assignment.ScopeId.Contains("/subscriptions/", StringComparison.OrdinalIgnoreCase)
            ? "subscription"
            : "managementgroup";

        return new ScopeFavorite(
            Guid.Empty,
            account.TenantId,
            eligibility.ResourceId,
            eligibility.ScopeId,
            [new EligibleChildScope(assignment.ScopeId, name, type)],
            DateTimeOffset.UtcNow,
            IsPinned: false);
    }

    /// <summary>
    /// The reason the expiring activation was granted on, marked as an extension —
    /// or nothing, when this session did not grant it (another machine, the portal,
    /// or an app restart in between).
    /// </summary>
    private string? CarriedJustificationFor(ActiveAssignmentItemViewModel item)
    {
        var key = PendingMatchKey(
            item.Account,
            item.Assignment.Kind,
            item.Assignment.ResourceId,
            item.Assignment.ScopeId);

        if (!_grantedJustifications.TryGetValue(key, out var justification))
        {
            return null;
        }

        // Extending an extension must not stack the suffix.
        return justification.EndsWith(ExtendJustificationSuffix, StringComparison.OrdinalIgnoreCase)
            ? justification
            : justification + ExtendJustificationSuffix;
    }

    /// <summary>
    /// Ends the activation the panel is replacing and waits until PIM has let go of
    /// it. <c>null</c> once it is gone, otherwise the reason to show in the panel.
    /// </summary>
    private async Task<UserFacingError?> EndRunningActivationAsync(SignedInAccount account, ActiveAssignment assignment)
    {
        var key = PendingMatchKey(account, assignment.Kind, assignment.ResourceId, assignment.ScopeId);

        // It may have run out while the user was filling the form in — the entry
        // point is an expiry warning, so that is an ordinary case here, not an edge
        // one. Nothing left to end, and the new activation is free to go. Same caveat
        // as in the wait below: a read that failed says nothing about the role.
        if (!_activeReadFailures.Contains(EnrollmentKey(account)) && !ActiveAssignmentExists(key))
        {
            return null;
        }

        try
        {
            using var cts = new CancellationTokenSource(GraphCallTimeout);
            var result = await _aggregator.DeactivateAsync(account, assignment, cts.Token);

            _logger.LogInformation(
                "Deactivation for re-activation submitted for {Role} (account oid {Oid}): requestId={RequestId}, status={Status}",
                assignment.DisplayName,
                account.ObjectId,
                result.RequestId,
                result.Status);

            if (result.Error is { } error)
            {
                // Same race, one refresh later: the list this method checked can be up
                // to a refresh interval old, so PIM refusing to end something that is
                // already over must not block the new activation. A read that failed
                // says nothing either way, and then the refusal stands.
                await RefreshAsync();
                var confirmedGone = !_activeReadFailures.Contains(EnrollmentKey(account))
                    && !ActiveAssignmentExists(key);
                return confirmedGone ? null : error;
            }

            if (result.Status is ActivationStatus.Failed or ActivationStatus.Denied)
            {
                return new UserFacingError(
                    ErrorSeverity.Fatal,
                    $"Microsoft Graph reported status {result.Status} when ending the running activation. It is still active.",
                    null);
            }

            // Best effort, not a gate. The read API is not the authority on whether
            // PIM will accept a new activation — measured on an Azure resource role,
            // the row was gone while every request was still refused for another
            // minute and a half, and it can just as well be the other way round. The
            // activation request itself is the authority, and it retries; blocking on
            // this would fail an extension that was about to work.
            if (!await WaitUntilInactiveAsync(account, key))
            {
                _logger.LogInformation(
                    "Release of {Role} unconfirmed after {Seconds}s; requesting the new activation anyway (account oid {Oid})",
                    assignment.DisplayName,
                    DeactivationWatchdog.TotalSeconds,
                    account.ObjectId);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ending the running activation failed");
            return PimErrorMapper.MapException(ex);
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

            // Only fire the success toast once the role has actually disappeared, so
            // we never falsely claim success.
            if (await WaitUntilInactiveAsync(item.Account, deactivationKey))
            {
                _toastService.ShowDeactivationResult(item.DisplayName, result);
                return;
            }

            // Watchdog: still active after ~60 s. Surface the Graph status so
            // the user can distinguish "PIM never processed it" from "PIM
            // accepted but the read API is just lagging".
            var waited = $"{DeactivationWatchdog.TotalSeconds:0}s";
            ClearDeactivatingState(deactivationKey);
            _toastService.ShowError(
                "Deactivation not confirmed",
                $"{item.DisplayName} still appears active after {waited} (Graph status: {result.Status}). Check the Entra PIM portal.");
            SetDeactivationErrorText(
                deactivationKey,
                $"Still active after {waited} — check the Entra PIM portal.");
            _logger.LogWarning(
                "Deactivation watchdog: {Role} still active {Seconds}s after request (account oid {Oid}, requestId {RequestId}, status {Status})",
                item.DisplayName,
                DeactivationWatchdog.TotalSeconds,
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

    /// <summary>
    /// Refreshes until the assignment matching <paramref name="key"/> is off the
    /// list, or the watchdog runs out. True when it is gone. Both paths that end an
    /// activation wait here: the stop button, which then reports success, and a
    /// re-activation, which then asks for the role again.
    /// </summary>
    private async Task<bool> WaitUntilInactiveAsync(
        SignedInAccount account,
        (PimResourceKind Kind, string ResourceId, string ScopeId, string ObjectId, string TenantId) key)
    {
        var enrollment = EnrollmentKey(account);
        var attempts = (int)(DeactivationWatchdog / DeactivationPollInterval);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            await Task.Delay(DeactivationPollInterval);
            await RefreshAsync();

            // A read that failed hands back an empty list, so "not in the list" would
            // mean "the network blinked" every bit as readily as "PIM let go". Only a
            // read that actually came back may end the wait.
            if (!_activeReadFailures.Contains(enrollment) && !ActiveAssignmentExists(key))
            {
                return true;
            }
        }

        return false;
    }

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

    private async void OnActivated(IReadOnlyList<ActivationOutcome> outcomes)
    {
        if (outcomes.Count == 0)
        {
            return;
        }

        // One toast per verdict, however many scopes: three "Owner is now active"
        // in a row read like a stutter, and "active" over a scope that is only
        // awaiting approval would be wrong — role settings differ per scope.
        foreach (var verdict in outcomes.GroupBy(outcome => outcome.Result.Status == ActivationStatus.PendingApproval))
        {
            var members = verdict.ToList();
            _toastService.ShowActivationResult(ToastLabel(members), members[0].Result);
        }

        if (outcomes.FirstOrDefault(outcome => outcome.Result.IsSuccess) is { } granted)
        {
            RememberRecent(granted.Account, granted.Request.Eligibility);
        }

        // Per scope, not per batch: a narrowed activation carries one reason across
        // several subscriptions, and each of them can be extended on its own later.
        foreach (var outcome in outcomes.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Request.Justification)))
        {
            var target = outcome.Target;
            _grantedJustifications[PendingMatchKey(outcome.Account, target.Kind, target.ResourceId, target.ScopeId)] =
                outcome.Request.Justification!;
        }

        // Graph PIM's read API is eventually consistent — show a placeholder
        // immediately, the next refresh swaps it for the real assignment
        // (or the 30 s watchdog drops it). One per scope for a narrowed
        // activation, each naming its own scope.
        var pendingShown = false;
        foreach (var outcome in outcomes.Where(candidate => ShouldShowPendingFor(candidate.Result)))
        {
            var duration = outcome.Result.EndDateTime is { } end
                ? end - DateTimeOffset.UtcNow
                : outcome.Request.Duration;
            var pending = ActiveAssignmentItemViewModel.CreatePending(
                outcome.Target,
                outcome.Account,
                duration,
                DeactivateAsync,
                ReactivateAsync);
            _tenantNameCache.TryGetValue(outcome.Account.TenantId, out var cachedName);
            pending.TenantName = cachedName;
            pending.AccountAlias = AliasFor(outcome.Account);
            pending.ScopeSubscriptionName = SubscriptionNameFor(outcome.Target.ScopeId);
            ActiveAssignments.Insert(0, pending);
            pendingShown = true;
        }

        if (pendingShown)
        {
            UpdateActiveCount();
            MarkActiveEligibilities();
        }

        await RefreshAsync();

        if (pendingShown)
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

    /// <summary>"Owner", "Owner · Subscription: lz-prod", or "Owner on 3 scopes".</summary>
    private string ToastLabel(IReadOnlyList<ActivationOutcome> outcomes)
    {
        var role = outcomes[0].Request.Eligibility.DisplayName;
        if (outcomes.Count > 1)
        {
            return $"{role} on {outcomes.Count} scopes";
        }

        return outcomes[0].IsNarrowed ? $"{role} · {outcomes[0].Target.ScopeLabel}" : role;
    }

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

        // A completed sign-in is the only real proof the App Registration is
        // set up correctly — it exercises the client id, public client flows,
        // the broker redirect URI and admin consent in one go. Record it against
        // the registration of the tenant that was signed into; the other
        // registrations are separate and prove nothing here.
        MarkAppRegistrationVerified([added]);

        await RefreshAsync();

        // Refresh rebuilt the groups; expand the new one (OnActiveAccountChanged
        // ran before the group existed so its expand call was a no-op).
        ExpandGroupFor(added);
    }

    /// <summary>
    /// Stamps the app registrations <paramref name="accounts"/> resolve to as proven —
    /// a sign-in against them actually succeeded. Ids already recorded are skipped,
    /// so the common case doesn't rewrite the settings file. All accounts are folded
    /// into a single write; two <see cref="PersistShellSettings"/> calls in a row
    /// would both read the same pre-write snapshot and one would lose.
    /// </summary>
    private void MarkAppRegistrationVerified(IEnumerable<SignedInAccount> accounts)
    {
        var verified = _userSettings.Current.VerifiedClientIds ?? [];
        var added = accounts
            .Select(a => _options.ClientIdFor(a.Cloud, a.TenantId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(id => !verified.Contains(id!, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (added.Length == 0)
        {
            return;
        }

        PersistShellSettings(s => s with { VerifiedClientIds = [.. verified, .. added!] });
        _settingsPanel.NotifyAppRegistrationVerificationChanged();
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

    /// <summary>
    /// The user's alias for an enrollment, or <c>null</c> when none is set.
    /// Read from settings at every use rather than cached in a field: this view
    /// model is constructed before <c>IUserSettingsService.LoadAsync()</c> runs
    /// (see <c>App.axaml.cs</c>), so a constructor-time snapshot would be empty.
    /// </summary>
    private string? AliasFor(SignedInAccount account)
        => _userSettings.Current.AccountAliases is { } aliases
            && aliases.TryGetValue(EnrollmentKey(account), out var alias)
                ? alias
                : null;

    private void UpdateCountdowns()
    {
        var settings = _userSettings.Current;

        foreach (var item in ActiveAssignments)
        {
            item.UpdateCountdown();
        }

        EvaluateExpiryWarnings(
            settings.ExpiryWarningEnabled,
            TimeSpan.FromMinutes(settings.ExpiryWarningMinutes));
    }

    /// <summary>
    /// Recomputes which active assignments sit inside the user-configured warning
    /// window and drives the two visible channels:
    /// <list type="bullet">
    /// <item>the standalone alert window — <see cref="IsExpiryAlertVisible"/> +
    /// <see cref="ExpiryAlert"/>, showing the soonest <em>non-dismissed</em>
    /// assignment;</item>
    /// <item>the tray indicator — <see cref="MostUrgentExpiring"/> +
    /// <see cref="ExpiringChanged"/>, reflecting the soonest assignment
    /// regardless of dismissal.</item>
    /// </list>
    /// Runs every countdown tick so the surfaced countdown ticks live.
    /// </summary>
    private void EvaluateExpiryWarnings(bool enabled, TimeSpan threshold)
    {
        if (!enabled)
        {
            MostUrgentExpiring = null;
            ExpiringCount = 0;
            _currentAlertKey = null;
            IsExpiryAlertVisible = false;
            RaiseExpiringChangedIfChanged();
            return;
        }

        // Settled rows only — a pending (Activating…) or deactivating row has no
        // meaningful "expires in" yet.
        var expiring = ActiveAssignments
            .Where(a => !a.IsPending && !a.IsDeactivating)
            .Where(a => a.RemainingTime > TimeSpan.Zero && a.RemainingTime <= threshold)
            .OrderBy(a => a.RemainingTime)
            .ToList();

        // Forget dismissals for assignments that have left the window (expired or
        // deactivated) so a later re-activation warns afresh.
        var liveKeys = expiring.Select(ExpiryKey).ToHashSet(StringComparer.Ordinal);
        _expiryDismissed.IntersectWith(liveKeys);

        MostUrgentExpiring = expiring.FirstOrDefault();
        ExpiringCount = expiring.Count;
        RaiseExpiringChangedIfChanged();

        var alertTarget = expiring.FirstOrDefault(a => !_expiryDismissed.Contains(ExpiryKey(a)));
        if (alertTarget is null)
        {
            _currentAlertKey = null;
            _currentAlertRow = null;
            IsExpiryAlertVisible = false;
            return;
        }

        _currentAlertKey = ExpiryKey(alertTarget);

        // Kept alongside the key because the alert's Re-activate acts on the row
        // itself; a refresh rebuilds the rows, so this is re-pointed every tick.
        _currentAlertRow = alertTarget;
        ExpiryAlert.UpdateFrom(alertTarget, expiring.Count - 1);
        IsExpiryAlertVisible = true;
    }

    /// <summary>
    /// Raises <see cref="ExpiringChanged"/> only when the surfaced state actually
    /// changes (most-urgent assignment, its minute/second label, or the count) so
    /// the tray isn't rewritten every single tick.
    /// </summary>
    private void RaiseExpiringChangedIfChanged()
    {
        var signature = MostUrgentExpiring is { } urgent
            ? $"{ExpiryKey(urgent)}|{urgent.RemainingText}|{ExpiringCount}"
            : "none";
        if (signature == _lastExpiringSignature)
        {
            return;
        }

        _lastExpiringSignature = signature;
        ExpiringChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Stable identity for an active assignment across refreshes. Prefers the
    /// PIM assignment-schedule id; falls back to (account, kind, resource, scope)
    /// for synthesized/pending rows that don't carry one yet. Instance method to
    /// match the existing key-builder convention (see <see cref="EnrollmentKey"/>).
    /// </summary>
    private string ExpiryKey(ActiveAssignmentItemViewModel item)
        => string.IsNullOrEmpty(item.Assignment.AssignmentScheduleId)
            ? $"{item.Account.ObjectId}|{(int)item.Assignment.Kind}|{item.Assignment.ResourceId}|{item.Assignment.ScopeId}"
            : item.Assignment.AssignmentScheduleId;

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

        var item = new AccountListItemViewModel(account, RenameAccount, SelectAccountCommand, RemoveAccountCommand)
        {
            AccountAlias = AliasFor(account),
        };
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
    /// Stores (or clears, when <paramref name="alias"/> is <c>null</c>) the user's
    /// name for one enrollment and pushes it to every row already on screen.
    /// </summary>
    /// <remarks>
    /// The push is not cosmetic: account rows are never rebuilt by a refresh, and
    /// the popup rows would otherwise carry the old name until the next 60 s tick.
    /// Matching is per enrollment, not per tenant id — two accounts in one tenant
    /// are named separately. The alias never reaches the log: it is text the user
    /// typed and may contain a UPN or a customer name.
    /// </remarks>
    private void RenameAccount(AccountListItemViewModel row, string? alias)
    {
        var key = EnrollmentKey(row.Account);
        PersistShellSettings(s =>
        {
            var dict = s.AccountAliases is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(s.AccountAliases, StringComparer.OrdinalIgnoreCase);
            if (alias is null)
            {
                dict.Remove(key);
            }
            else
            {
                dict[key] = alias;
            }

            return s with { AccountAliases = dict };
        });

        // PersistShellSettings only publishes Current after the async write, so a
        // refresh landing inside that window rebuilds rows with the previous alias
        // and self-heals on the next tick. The rows below are updated regardless.
        row.AccountAlias = alias;

        foreach (var active in ActiveAssignments)
        {
            if (IsSameEnrollment(active.Account, row.Account))
            {
                active.AccountAlias = alias;
            }
        }

        foreach (var group in EligibilityGroups)
        {
            if (IsSameEnrollment(group.Account, row.Account))
            {
                group.AccountAlias = alias;
            }
        }
    }

    /// <summary>
    /// Rebuilds <see cref="EligibilityGroups"/> from the aggregated dict.
    /// Preserves <see cref="TenantEligibilityGroup.IsExpanded"/> across
    /// rebuilds so a refresh doesn't snap the user's open/closed layout shut.
    /// </summary>
    private void BuildEligibilityGroups(
        IReadOnlyDictionary<SignedInAccount, EligibilityFetchResult> aggregated)
    {
        _pinnedKeys = new HashSet<string>(
            _userSettings.Current.PinnedEligibilities ?? [], StringComparer.Ordinal);

        // Snapshot prior expansion keyed by (oid, tid) so we can carry it
        // forward — refresh rebuilds the group instances.
        var previousExpansion = EligibilityGroups
            .ToDictionary(g => EnrollmentKey(g.Account), g => g.IsExpanded);

        // Same for the role nodes inside each group, keyed by tenant + role. Kept in
        // memory only: a node the user opened must survive the 60 s refresh, but a
        // tenant with hundreds of Azure roles has no business filling the settings file.
        // Merged, not replaced. A tenant that failed to fetch this tick renders with no
        // rows at all, so rebuilding these from what is on screen would forget what the
        // user opened there and collapse it again once the tenant comes back — the one
        // case the memory exists for.
        foreach (var group in EligibilityGroups)
        {
            foreach (var node in group.RoleGroups)
            {
                _roleGroupExpansion[RoleNodeKey(group.Account, node.RoleDefinitionId)] = node.IsExpanded;
            }

            foreach (var section in group.Sections)
            {
                _sectionExpansion[SectionKey(group.Account, section.Kind)] = section.IsExpanded;
            }
        }

        EligibilityGroups.Clear();

        // Order groups by the Accounts collection order (which itself is the
        // enrollment order) so the layout is stable across refreshes.
        foreach (var accountItem in Accounts)
        {
            var account = accountItem.Account;
            if (!aggregated.TryGetValue(account, out var fetched))
            {
                fetched = new EligibilityFetchResult(Array.Empty<PimEligibility>(), null);
            }

            _tenantNameCache.TryGetValue(account.TenantId, out var cachedName);

            var group = new TenantEligibilityGroup(account)
            {
                SuppressUserExpansionEvent = true,
                TenantName = cachedName,
                AccountAlias = AliasFor(account),
                LoadError = fetched.LoadError,
            };
            BuildGroupRows(group, fetched.Items, account, cachedName);

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
                // Collapsed by default, including the active account's. With several
                // hundred eligibilities an expanded tenant is the whole popup; what the
                // user should land on is the pinned and recent entries above it.
                group.IsExpanded = false;
            }

            group.SuppressUserExpansionEvent = false;
            group.ExpansionToggledByUser += value => OnGroupExpansionToggledByUser(group, value);

            EligibilityGroups.Add(group);
        }

        RebuildShortcutSections();
        UpdateEligibleCount();
    }

    /// <summary>
    /// Fills one tenant group: the canonical flat <see cref="TenantEligibilityGroup.Items"/>
    /// list, plus the two collections the view renders — plain rows, and one node per
    /// Azure resource role the account holds on more than one scope.
    /// </summary>
    /// <remarks>
    /// The folding is what keeps the list usable for infrastructure teams: eligible
    /// on one role across 400 subscriptions is 400 rows otherwise, and no amount of
    /// scrolling makes that readable. A role on a single scope stays a plain row —
    /// a node hiding one child would only cost a click.
    /// </remarks>
    private void BuildGroupRows(
        TenantEligibilityGroup group,
        IReadOnlyList<PimEligibility> eligibilities,
        SignedInAccount account,
        string? tenantName)
    {
        var foldable = eligibilities
            .Where(e => e.Kind == PimResourceKind.AzureResourceRole)
            .GroupBy(e => RoleDefinitionKey(e.ResourceId), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        // Sections start collapsed once a tenant is big enough that scrolling it is the
        // problem; below that, opening the tenant should still show its roles directly.
        var sectionsStartOpen = eligibilities.Count <= SectionAutoExpandLimit;

        foreach (var eligibility in eligibilities)
        {
            var folded = eligibility.Kind == PimResourceKind.AzureResourceRole
                && foldable.ContainsKey(RoleDefinitionKey(eligibility.ResourceId));
            var row = new EligibilityItemViewModel(eligibility, account, ActivateAsync, TogglePin)
            {
                TenantName = tenantName,
                IsInRoleGroup = folded,
                IsPinned = _pinnedKeys.Contains(ShortcutKey(account, eligibility)),
            };

            group.Items.Add(row);
            var section = SectionFor(group, SectionKindOf(eligibility), sectionsStartOpen);
            if (!folded)
            {
                section.Items.Add(row);
                continue;
            }

            var roleKey = RoleDefinitionKey(eligibility.ResourceId);
            var node = section.RoleGroups.FirstOrDefault(
                r => string.Equals(r.RoleDefinitionId, roleKey, StringComparison.OrdinalIgnoreCase));
            if (node is null)
            {
                node = new AzureRoleGroup(roleKey, eligibility.DisplayName)
                {
                    IsExpanded = _roleGroupExpansion.TryGetValue(
                        RoleNodeKey(account, roleKey), out var wasExpanded) && wasExpanded,
                };
                section.RoleGroups.Add(node);
            }

            node.Items.Add(row);
            node.MatchCount = node.Items.Count;
        }
    }

    /// <summary>
    /// Which section an eligibility belongs to. An administrative-unit-scoped directory
    /// role is deliberately not filed under "Directory roles": its scope is not the
    /// directory, and the section header says so once instead of every row repeating it.
    /// </summary>
    private EligibilitySectionKind SectionKindOf(PimEligibility eligibility) => eligibility.Kind switch
    {
        PimResourceKind.DirectoryRole when eligibility.IsAdministrativeUnitScoped
            => EligibilitySectionKind.AdministrativeUnit,
        PimResourceKind.DirectoryRole => EligibilitySectionKind.DirectoryRole,
        PimResourceKind.AzureResourceRole => EligibilitySectionKind.AzureResource,

        // Membership and ownership share a section; the row's own kind line is the flag.
        _ => EligibilitySectionKind.Group,
    };

    /// <summary>
    /// The tenant's section for <paramref name="kind"/>, created on first use so a tenant
    /// only ever shows headers for what it actually holds. Order follows the enum.
    /// </summary>
    private EligibilitySection SectionFor(TenantEligibilityGroup group, EligibilitySectionKind kind, bool startExpanded)
    {
        var existing = group.Sections.FirstOrDefault(s => s.Kind == kind);
        if (existing is not null)
        {
            return existing;
        }

        var section = new EligibilitySection(kind)
        {
            IsExpanded = _sectionExpansion.TryGetValue(SectionKey(group.Account, kind), out var wasExpanded)
                ? wasExpanded
                : startExpanded,
        };

        var insertAt = group.Sections.Count(s => s.Kind < kind);
        group.Sections.Insert(insertAt, section);
        return section;
    }

    /// <summary>
    /// Identity of one collapsible Azure role node. Keyed by enrollment, not by
    /// tenant: a normal and an admin account in the same tenant are two groups
    /// that can hold a node for the same role.
    /// </summary>
    private string RoleNodeKey(SignedInAccount account, string roleDefinitionId)
        => $"{EnrollmentKey(account)}|{roleDefinitionId}";

    /// <summary>
    /// Identity of one kind section, so an opened section survives the 60 s refresh.
    /// Keyed by enrollment, like the role nodes — two accounts in one tenant are two
    /// groups, each with its own sections.
    /// </summary>
    private string SectionKey(SignedInAccount account, EligibilitySectionKind kind)
        => $"{EnrollmentKey(account)}|{(int)kind}";

    /// <summary>
    /// The role's own identity, without the scope it was read at. ARM returns
    /// <c>roleDefinitionId</c> fully qualified — the same built-in role reads as a
    /// different string under every subscription — so the trailing GUID is what
    /// makes "Owner on 400 subscriptions" one role instead of 400.
    /// </summary>
    private string RoleDefinitionKey(string resourceId)
        => resourceId[(resourceId.LastIndexOf('/') + 1)..];

    /// <summary>
    /// Identity of one (enrollment, kind, role, scope) eligibility, used as the
    /// persisted key for pins and the recent list.
    /// </summary>
    private string ShortcutKey(SignedInAccount account, PimEligibility eligibility)
        => $"{EnrollmentKey(account)}|{(int)eligibility.Kind}|{eligibility.ResourceId}|{eligibility.ScopeId}";

    /// <summary>
    /// Refills the pinned and recent sections from the live rows. Both are views over
    /// what the tenant groups actually hold: a key whose eligibility is gone — revoked,
    /// or its tenant failing to load — simply drops out instead of offering a row that
    /// would fail on click. Recent skips anything pinned so nothing shows up twice.
    /// PINNED holds both kinds of shortcut: eligibilities, and the saved scope sets
    /// over them that the user starred — one list, because that is what the star means.
    /// </summary>
    private void RebuildShortcutSections()
    {
        PinnedItems.Clear();
        RecentItems.Clear();

        // Grouped, not ToDictionary: an eligibility held directly *and* through a
        // group comes back as two rows with one key, and a duplicate-key throw here
        // would take down the whole refresh, not just the shortcut sections.
        var live = EligibilityGroups
            .SelectMany(group => group.Items)
            .GroupBy(row => ShortcutKey(row.Account, row.Eligibility), StringComparer.Ordinal)
            .ToDictionary(rows => rows.Key, rows => rows.First(), StringComparer.Ordinal);

        foreach (var key in _pinnedKeys)
        {
            if (live.TryGetValue(key, out var row))
            {
                PinnedItems.Add(Shortcut(row));
            }
        }

        foreach (var favorite in _scopeFavorites.Current
            .Where(favorite => favorite.IsPinned)
            .OrderBy(favorite => favorite.CreatedAt))
        {
            var row = live.Values.FirstOrDefault(candidate => favorite.BelongsTo(
                candidate.Account.ObjectId,
                candidate.Account.TenantId,
                candidate.Eligibility.ResourceId,
                candidate.Eligibility.ScopeId));
            if (row is not null)
            {
                PinnedItems.Add(Shortcut(row, favorite));
            }
        }

        foreach (var key in _userSettings.Current.RecentEligibilities ?? [])
        {
            if (RecentItems.Count == RecentShortcutLimit)
            {
                break;
            }

            if (!_pinnedKeys.Contains(key) && live.TryGetValue(key, out var row))
            {
                RecentItems.Add(Shortcut(row));
            }
        }

        UpdateShortcutVisibility();
    }

    /// <summary>One place for the rule: shortcuts exist and no search is running.</summary>
    private void UpdateShortcutVisibility()
        => HasShortcuts = (PinnedItems.Count > 0 || RecentItems.Count > 0)
            && string.IsNullOrWhiteSpace(FilterText);

    /// <summary>A second row view model over the same eligibility, laid out for the top sections.</summary>
    private EligibilityItemViewModel Shortcut(EligibilityItemViewModel row, ScopeFavorite? favorite = null)
        => new(row.Eligibility, row.Account, ActivateAsync, TogglePin)
        {
            TenantName = row.TenantName,
            IsShortcut = true,

            // A favourite row is on the list because it is starred, and it is never
            // dimmed: it stands for several scopes, and the eligibility above them
            // being active says nothing about whether those are.
            IsPinned = favorite is not null || row.IsPinned,
            IsCurrentlyActive = favorite is null && row.IsCurrentlyActive,
            Favorite = favorite,
        };

    /// <summary>Pins or unpins one eligibility — or one saved scope set — and rebuilds the sections.</summary>
    private void TogglePin(EligibilityItemViewModel row)
    {
        if (row.Favorite is { } favorite)
        {
            // The store's Changed event rebuilds the sections; nothing to do here.
            _ = SafeSetFavoritePinnedAsync(favorite);
            return;
        }

        var key = ShortcutKey(row.Account, row.Eligibility);
        if (!_pinnedKeys.Remove(key))
        {
            _pinnedKeys.Add(key);
        }

        foreach (var candidate in EligibilityGroups.SelectMany(group => group.Items))
        {
            if (ShortcutKey(candidate.Account, candidate.Eligibility) == key)
            {
                candidate.IsPinned = _pinnedKeys.Contains(key);
            }
        }

        PersistShellSettings(s => s with { PinnedEligibilities = [.. _pinnedKeys] });
        RebuildShortcutSections();
    }

    /// <summary>
    /// Records a successful activation at the head of the recent list. Kept longer
    /// than it is shown so an entry that is currently pinned or unavailable can
    /// resurface instead of being lost.
    /// </summary>
    private void RememberRecent(SignedInAccount account, PimEligibility eligibility)
    {
        var key = ShortcutKey(account, eligibility);
        PersistShellSettings(s =>
        {
            var recent = new List<string> { key };
            recent.AddRange((s.RecentEligibilities ?? []).Where(existing => existing != key));
            return s with { RecentEligibilities = [.. recent.Take(RecentShortcutMemory)] };
        });
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
        UpdateShortcutVisibility();

        // Entering filter mode → snapshot. Leaving filter mode → restore.
        if (filterActive && _preFilterExpansion is null)
        {
            _preFilterExpansion = EligibilityGroups.ToDictionary(
                g => EnrollmentKey(g.Account),
                g => g.IsExpanded);
            _preFilterSectionExpansion = EligibilityGroups
                .SelectMany(g => g.Sections.Select(sec => (Key: SectionKey(g.Account, sec.Kind), sec.IsExpanded)))
                .ToDictionary(entry => entry.Key, entry => entry.IsExpanded, StringComparer.Ordinal);
        }
        else if (!filterActive && _preFilterExpansion is { } snapshot)
        {
            foreach (var group in EligibilityGroups)
            {
                group.SuppressUserExpansionEvent = true;
                try
                {
                    group.IsExpanded = snapshot.TryGetValue(EnrollmentKey(group.Account), out var prior)
                        && prior;
                }
                finally
                {
                    group.SuppressUserExpansionEvent = false;
                }

                // Role nodes and sections collapse again when the search ends. The
                // search opened them; leaving hundreds of rows unfolded would undo the
                // very thing they exist for.
                foreach (var node in group.RoleGroups)
                {
                    node.IsExpanded = false;
                }

                foreach (var section in group.Sections)
                {
                    section.IsExpanded = _preFilterSectionExpansion is { } sections
                        && sections.TryGetValue(SectionKey(group.Account, section.Kind), out var priorSection)
                        && priorSection;
                }
            }

            _preFilterExpansion = null;
            _preFilterSectionExpansion = null;
        }

        foreach (var group in EligibilityGroups)
        {
            var matches = 0;
            foreach (var item in group.Items)
            {
                item.IsVisible = !filterActive || item.Matches(filter);
                if (item.IsVisible)
                {
                    matches++;
                }
            }

            // A role node counts and shows only its matching scopes, and opens itself
            // while a filter is active — a hit behind a collapsed node reads as "no result".
            foreach (var node in group.RoleGroups)
            {
                node.MatchCount = node.Items.Count(item => item.IsVisible);
                node.IsVisible = node.MatchCount > 0;
                if (filterActive)
                {
                    node.IsExpanded = true;
                }
            }

            // Same rule one level up: a section counts only its matching rows, hides when
            // it has none, and opens itself while a filter is active.
            foreach (var section in group.Sections)
            {
                section.MatchCount = section.Items.Count(item => item.IsVisible)
                    + section.RoleGroups.Sum(node => node.MatchCount);
                section.IsVisible = !filterActive || section.MatchCount > 0;
                if (filterActive)
                {
                    section.IsExpanded = true;
                }
            }

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

    /// <summary>Stars or unstars a saved scope set, logging rather than throwing on a failed write.</summary>
    private async Task SafeSetFavoritePinnedAsync(ScopeFavorite favorite)
    {
        try
        {
            await _scopeFavorites.SetPinnedAsync(favorite.Id, !favorite.IsPinned).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Saving the starred state of a scope favourite failed");
        }
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
        IReadOnlyDictionary<SignedInAccount, ActiveAssignmentFetchResult> aggregated)
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
        // Indexer, not ToDictionary: two active rows can share a key — the same Azure
        // role at the same scope, held directly and through a group — and a duplicate
        // there would throw the whole refresh away over a carried-over caption.
        var errorTexts = new Dictionary<(PimResourceKind, string, string, string, string), string?>();
        foreach (var row in ActiveAssignments.Where(a => !string.IsNullOrEmpty(a.DeactivationErrorText)))
        {
            errorTexts[PendingMatchKey(row.Account, row.Assignment.Kind, row.Assignment.ResourceId, row.Assignment.ScopeId)] =
                row.DeactivationErrorText;
        }

        ActiveAssignments.Clear();

        var addedKeys = new HashSet<(PimResourceKind, string, string, string, string)>();
        foreach (var (account, fetched) in aggregated)
        {
            _tenantNameCache.TryGetValue(account.TenantId, out var cachedName);
            var alias = AliasFor(account);

            foreach (var assignment in fetched.Items)
            {
                var key = PendingMatchKey(account, assignment.Kind, assignment.ResourceId, assignment.ScopeId);
                errorTexts.TryGetValue(key, out var carriedError);
                ActiveAssignments.Add(new ActiveAssignmentItemViewModel(assignment, account, DeactivateAsync, ReactivateAsync)
                {
                    TenantName = cachedName,
                    ScopeSubscriptionName = SubscriptionNameFor(assignment.ScopeId),
                    AccountAlias = alias,
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

    /// <summary>
    /// Identity of one assignment or eligibility for matching active rows against
    /// pending rows and eligibility rows. An Azure role is compared by its definition
    /// GUID alone and case-insensitively: an activation narrowed to a subscription is
    /// requested with the management group's <c>roleDefinitionId</c>, the row ARM lists
    /// afterwards may carry the subscription's, and ARM scope ids are not case-sensitive
    /// — the same role at the same scope either way.
    /// </summary>
    private (PimResourceKind Kind, string ResourceId, string ScopeId, string ObjectId, string TenantId)
        PendingMatchKey(SignedInAccount account, PimResourceKind kind, string resourceId, string scopeId)
        => kind == PimResourceKind.AzureResourceRole
            ? (kind, RoleDefinitionKey(resourceId).ToLowerInvariant(), scopeId.ToLowerInvariant(), account.ObjectId, account.TenantId)
            : (kind, resourceId, scopeId, account.ObjectId, account.TenantId);

    /// <summary>
    /// Flags any eligibility row whose (Kind, ResourceId, ScopeId, oid, tid)
    /// tuple matches an active assignment so the row can be dimmed and made
    /// non-clickable. Composite key includes account so the same identity in
    /// two tenants never cross-poisons rows; built by <see cref="PendingMatchKey"/>
    /// so Azure rows match the way pending rows do.
    /// </summary>
    private void MarkActiveEligibilities()
    {
        var activeKeys = ActiveAssignments
            .Select(row => PendingMatchKey(row.Account, row.Assignment.Kind, row.Assignment.ResourceId, row.Assignment.ScopeId))
            .ToHashSet();

        foreach (var group in EligibilityGroups)
        {
            foreach (var item in group.Items)
            {
                item.IsCurrentlyActive = activeKeys.Contains(
                    PendingMatchKey(item.Account, item.Eligibility.Kind, item.Eligibility.ResourceId, item.Eligibility.ScopeId));
            }

            // Counted off the canonical flat list, so it stays correct no matter how
            // the sections below split the same rows up.
            group.ActiveCount = group.Items.Count(item => item.IsCurrentlyActive);

            foreach (var node in group.RoleGroups)
            {
                node.ActiveCount = node.Items.Count(item => item.IsCurrentlyActive);
            }

            // A collapsed section must still admit that something behind it is granting
            // access right now — the count is over the section's own rows, whether they
            // sit directly in it or inside one of its role nodes.
            foreach (var section in group.Sections)
            {
                section.ActiveCount = section.Items.Concat(section.RoleGroups.SelectMany(n => n.Items))
                    .Count(item => item.IsCurrentlyActive);
            }
        }

        // The shortcut sections hold their own row instances over the same
        // eligibilities, so they need the same pass.
        foreach (var item in PinnedItems.Concat(RecentItems).Where(item => item.Favorite is null))
        {
            item.IsCurrentlyActive = activeKeys.Contains(
                PendingMatchKey(item.Account, item.Eligibility.Kind, item.Eligibility.ResourceId, item.Eligibility.ScopeId));
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
