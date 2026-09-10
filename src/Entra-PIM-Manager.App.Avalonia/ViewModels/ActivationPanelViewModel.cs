namespace EntraPimManager.AppAvalonia.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Collections;
using EntraPimManager.Core.ErrorHandling;
using EntraPimManager.Core.Models;
using EntraPimManager.Core.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// View model for the slide-in activation panel. Replaces the modal
/// <c>ActivationDialog</c> from the WPF UI. The panel supports two flows:
///
/// <list type="bullet">
/// <item>
/// <b>Validate</b> — posts the request with <see cref="ActivationRequest.IsValidationOnly"/>
/// set, so the API does a dry-run and reports any policy violations / approval
/// requirement / MFA challenge without actually activating.
/// </item>
/// <item>
/// <b>Activate</b> — the real activation; on success the slide-in closes and
/// the shell shows a toast + refresh.
/// </item>
/// </list>
///
/// An Azure resource role held on a management group must additionally say where it
/// is being activated: the "Activate on" picker starts empty and offers the
/// subscriptions beneath the scope, then the management groups, and last the whole
/// scope itself. Nothing is preselected on purpose — the widest grant should be a
/// decision, never the path of least resistance. Activate then submits one request
/// per ticked scope, and a ticked set can be saved as a favourite.
/// </summary>
public sealed partial class ActivationPanelViewModel : ObservableObject
{
    /// <summary>Time budget for the activation / validation Graph call.</summary>
    private static readonly TimeSpan GraphCallTimeout = TimeSpan.FromSeconds(30);

    // ponytail: a human MFA step-up (WAM prompt) cannot finish inside the 30 s
    // Graph budget — give auth-context activations room to complete interaction.
    private static readonly TimeSpan StepUpTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Budget for the scope picker's hierarchy walk: one paged read per management
    /// group, level by level, so a deep estate that hits a throttle needs more than a
    /// single call's 30 s.
    /// </summary>
    private static readonly TimeSpan ScopeWalkTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long a walked hierarchy is reused across openings of the same eligibility.
    /// A favourite row on the main page is meant to be one click, not one walk; a
    /// subscription created in the meantime shows up after this.
    /// </summary>
    private static readonly TimeSpan ScopeCacheTtl = TimeSpan.FromMinutes(10);

    private readonly IEligibilityAggregator _aggregator;
    private readonly IJustificationFavoritesStore _favoritesStore;
    private readonly IScopeFavoritesStore _scopeFavorites;
    private readonly IUserSettingsService _userSettings;
    private readonly ILogger<ActivationPanelViewModel> _logger;

    /// <summary>Walked hierarchies by <c>{tenant}|{scope}</c>, with when they were read.</summary>
    private readonly Dictionary<string, (DateTimeOffset ReadAt, IReadOnlyList<EligibleChildScope> Scopes)> _scopeCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Bumped by every <see cref="Open"/>. A scope read that finishes for an earlier
    /// generation belongs to a panel that is gone — even when it is the same role
    /// opened again — and is discarded.
    /// </summary>
    private int _generation;

    /// <summary>
    /// A favourite that could not be applied yet because the picker's list is not
    /// there. Until it is applied, Activate refuses: the row promised a narrowed
    /// activation, and the entire management group is the opposite of that.
    /// </summary>
    private ScopeFavorite? _pendingFavorite;

    /// <summary>True after a failed read; the next click on the picker tries again.</summary>
    private bool _scopeLoadFailed;

    /// <summary>True while the picker's exclusivity rule is re-ticking rows itself.</summary>
    private bool _syncingSelection;

    [ObservableProperty]
    private PimEligibility? _eligibility;

    [ObservableProperty]
    private SignedInAccount? _account;

    [ObservableProperty]
    private ActivationPolicy _policy = new();

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private double _durationHours = 1.0;

    [ObservableProperty]
    private string _justification = string.Empty;

    [ObservableProperty]
    private string _ticketNumber = string.Empty;

    [ObservableProperty]
    private string _ticketSystem = string.Empty;

    [ObservableProperty]
    private string? _validationMessage;

    [ObservableProperty]
    private string? _validationSuccess;

    /// <summary>True while a dry-run (<c>Validate</c>) is in flight.</summary>
    [ObservableProperty]
    private bool _isValidating;

    /// <summary>True while a real activation (<c>Submit</c>) is in flight.</summary>
    [ObservableProperty]
    private bool _isSubmitting;

    /// <summary>
    /// True while the activation goes to the eligibility's own scope — everything
    /// beneath it at once. Ticking it clears the individual scopes and vice versa, and
    /// it starts off: an activation has to name where it applies.
    /// </summary>
    [ObservableProperty]
    private bool _isEntireScope;

    /// <summary>True while the scope picker's list is being read.</summary>
    [ObservableProperty]
    private bool _isLoadingScopes;

    /// <summary>
    /// Why the scope list is not what it should be: a failed read, an empty hierarchy,
    /// a favourite naming scopes that are gone, or a favourite that could not be saved.
    /// Shown under the picker, not inside its flyout — a closed flyout hides nothing
    /// the user needs more than this.
    /// </summary>
    [ObservableProperty]
    private string? _scopeNotice;

    /// <summary>Search text over the scope names.</summary>
    [ObservableProperty]
    private string _scopeFilterText = string.Empty;

    /// <summary>The name typed for the favourite about to be saved; optional.</summary>
    [ObservableProperty]
    private string _newFavoriteName = string.Empty;

    public ActivationPanelViewModel(
        IEligibilityAggregator aggregator,
        IJustificationFavoritesStore favoritesStore,
        IScopeFavoritesStore scopeFavorites,
        IUserSettingsService userSettings,
        ILogger<ActivationPanelViewModel> logger)
    {
        _aggregator = aggregator;
        _favoritesStore = favoritesStore;
        _scopeFavorites = scopeFavorites;
        _userSettings = userSettings;
        _logger = logger;
    }

    /// <summary>
    /// Raised once per Activate click with everything that went through, right after
    /// the batch — whether the panel then closes or stays open because a later scope
    /// failed. Never raised empty.
    /// </summary>
    public event Action<IReadOnlyList<ActivationOutcome>>? Activated;

    /// <summary>
    /// True while any submit is in flight. Disables every footer button — and the
    /// scope picker, whose ticks the running batch has already read — so the user
    /// can't fire a second request or change what the first one is doing.
    /// </summary>
    public bool IsBusy => IsValidating || IsSubmitting;

    /// <summary>Display name of the resource currently being activated.</summary>
    public string ResourceName => Eligibility?.DisplayName ?? string.Empty;

    /// <summary>Display name of the account the activation runs under (tenant context).</summary>
    public string AccountLabel => Account is null
        ? string.Empty
        : Account.DisplayName ?? Account.Username;

    /// <summary>Upper bound for the duration slider, in hours.</summary>
    public double MaxDurationHours => Policy.MaximumDuration.TotalHours;

    /// <summary>Whether a justification must be provided.</summary>
    public bool RequiresJustification => Policy.RequiresJustification;

    /// <summary>Whether ticket information must be provided.</summary>
    public bool RequiresTicket => Policy.RequiresTicketInfo;

    /// <summary>Whether to show the "requires approval" banner.</summary>
    public bool ShowApprovalBanner => Policy.RequiresApproval;

    /// <summary>Whether to show the "may require step-up verification" banner.</summary>
    public bool ShowAuthContextBanner => Policy.RequiresAuthContext;

    /// <summary>
    /// Whether to warn that this group can carry directory roles.
    /// </summary>
    /// <remarks>
    /// It belongs here rather than on the list row. Joining a role-assignable group can
    /// grant far more than group membership, and this is the screen where the user
    /// commits to that — a badge back in the list is read while browsing, if at all.
    /// The banner names the consequence instead of Microsoft's term for it, which tells
    /// nobody anything who does not already know the term.
    /// </remarks>
    public bool ShowRoleAssignableBanner => Eligibility?.IsRoleAssignableGroup ?? false;

    /// <summary>
    /// Whether the Validate (dry-run) button applies. Azure Resource Manager has
    /// no validation-only mode, so the button is hidden for Azure resource roles.
    /// </summary>
    public bool CanValidate => Eligibility?.Kind != PimResourceKind.AzureResourceRole;

    /// <summary>Scope of the eligibility as the list shows it, e.g. <c>Management group: Tenant Root Group</c>.</summary>
    public string ScopeLabel => Eligibility?.ScopeLabel ?? string.Empty;

    /// <summary>Whether the "Activate on" picker applies — an Azure role held on a management group.</summary>
    public bool CanNarrowScope => Eligibility?.CanNarrowScope ?? false;

    /// <summary>The management groups and subscriptions beneath the eligibility's scope, once read.</summary>
    public ObservableCollection<ScopeOptionViewModel> ScopeOptions { get; } = [];

    /// <summary>The ticked scopes, shown as chips under the picker; empty while the entire scope is chosen.</summary>
    public ObservableCollection<ScopeOptionViewModel> SelectedScopeChips { get; } = [];

    /// <summary>Saved scope sets for the open eligibility; a click ticks their scopes.</summary>
    public ObservableCollection<ScopeFavorite> ScopeFavorites { get; } = [];

    /// <summary>How many scopes are ticked.</summary>
    public int SelectedScopeCount => SelectedScopeChips.Count;

    /// <summary>The picker button's text: nothing yet, the entire scope, or how many scopes are ticked.</summary>
    public string ScopeSummary => IsEntireScope
        ? $"{ScopeLabel} (entire scope)"
        : SelectedScopeCount switch
        {
            0 => "Select management groups or subscriptions…",
            1 => "1 scope selected",
            _ => $"{SelectedScopeCount} scopes selected",
        };

    /// <summary>True while the picker has no answer yet — the placeholder is greyed out.</summary>
    public bool HasNoScopeSelection => !IsEntireScope && SelectedScopeCount == 0;

    /// <summary>
    /// True when the ticked scopes can be saved as a new favourite — at least one,
    /// and not a set that is already saved.
    /// </summary>
    public bool CanSaveScopeSelection => SelectedScopeCount > 0
        && ScopeFavorites.All(favorite => !favorite.HasSameScopes(SelectedScopeChips.Select(option => option.Scope.Id)));

    /// <summary>Mirrors <see cref="HasFavoritesOrSaveable"/> for the scope favourites section.</summary>
    public bool HasScopeFavoritesOrSaveable => ScopeFavorites.Count > 0 || CanSaveScopeSelection;

    /// <summary>
    /// Saved justification templates for the currently open eligibility.
    /// Refreshed by <see cref="LoadFavoritesAsync"/> each time <see cref="Open"/>
    /// fires; clicking a chip pastes the favourite into <see cref="Justification"/>.
    /// </summary>
    public ObservableCollection<JustificationFavoriteViewModel> JustificationFavorites { get; } = [];

    /// <summary>
    /// True when the current <see cref="Justification"/> can be saved as a new
    /// favourite — non-empty and not yet stored for this role. The "Save" chip
    /// binds its <see cref="Avalonia.Controls.Visual.IsVisible"/> to this.
    /// </summary>
    public bool CanSaveCurrentJustification
        => !string.IsNullOrWhiteSpace(Justification)
            && JustificationFavorites.All(f =>
                !string.Equals(f.FullText, Justification.Trim(), StringComparison.Ordinal));

    /// <summary>
    /// True when the favourites section should be visible at all — either
    /// the user has saved chips for this role, or they've typed something
    /// that is saveable. The section's outer container binds its
    /// <see cref="Avalonia.Controls.Visual.IsVisible"/> to this so the
    /// divider + caption don't squat empty space.
    /// </summary>
    public bool HasFavoritesOrSaveable
        => JustificationFavorites.Count > 0 || CanSaveCurrentJustification;

    /// <summary>
    /// X-offset for the slide-in overlay's <c>TranslateTransform</c>. Driven by
    /// <see cref="IsOpen"/> and consumed by a <c>DoubleTransition</c> in XAML
    /// so the panel animates in (0) and out (420 = a bit wider than the popup).
    /// </summary>
    public double PanelOffsetX => IsOpen ? 0 : 420;

    /// <summary>
    /// Prepares the panel for a new activation and slides it in. With
    /// <paramref name="preselect"/>, a favourite's scopes are ticked as soon as the
    /// picker's list is there — the main page's FAVOURITES rows open the panel this way.
    /// </summary>
    public void Open(SignedInAccount account, PimEligibility eligibility, ActivationPolicy policy, ScopeFavorite? preselect = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(eligibility);
        ArgumentNullException.ThrowIfNull(policy);

        _generation++;
        Account = account;
        Eligibility = eligibility;
        Policy = policy;

        // Default duration comes from user settings, clamped to the role's
        // policy ceiling so the slider never starts above the maximum.
        var defaultDuration = _userSettings.Current.DefaultDurationHours;
        DurationHours = Math.Min(defaultDuration, policy.MaximumDuration.TotalHours);
        Justification = string.Empty;
        TicketNumber = string.Empty;

        // The ticket number is per incident and must never be carried over; the
        // system is a constant of the tenant, so it comes prefilled from settings.
        TicketSystem = _userSettings.Current.TicketSystemFor(account.TenantId) ?? string.Empty;
        ValidationMessage = null;
        ValidationSuccess = null;
        IsValidating = false;
        IsSubmitting = false;
        JustificationFavorites.Clear();
        OnPropertyChanged(nameof(CanSaveCurrentJustification));
        OnPropertyChanged(nameof(HasFavoritesOrSaveable));

        // The picker always starts empty: a narrowed selection is a decision per
        // activation, and so is taking the whole management group.
        IsLoadingScopes = false;
        ScopeNotice = null;
        ScopeFilterText = string.Empty;
        NewFavoriteName = string.Empty;
        ScopeOptions.Clear();

        // Only where there is a picker to apply it in: a favourite left pending on an
        // eligibility that cannot narrow would block Activate with no way out.
        _pendingFavorite = eligibility.CanNarrowScope ? preselect : null;
        _scopeLoadFailed = false;
        _syncingSelection = true;
        IsEntireScope = false;
        _syncingSelection = false;
        LoadScopeFavorites(account, eligibility);
        NotifyScopeSelectionChanged();
        IsOpen = true;

        if (eligibility.CanNarrowScope)
        {
            // Read at once, not on the first click on the picker: the picker is the
            // normal path now, and a landing zone is several reads deep.
            _ = LoadScopesAsync(account, eligibility);
        }

        _ = LoadFavoritesAsync(eligibility, account);
    }

    partial void OnIsOpenChanged(bool value) => OnPropertyChanged(nameof(PanelOffsetX));

    partial void OnAccountChanged(SignedInAccount? value) => OnPropertyChanged(nameof(AccountLabel));

    partial void OnEligibilityChanged(PimEligibility? value)
    {
        OnPropertyChanged(nameof(ResourceName));
        OnPropertyChanged(nameof(CanValidate));
        OnPropertyChanged(nameof(ShowRoleAssignableBanner));
        OnPropertyChanged(nameof(CanNarrowScope));
        OnPropertyChanged(nameof(ScopeLabel));
    }

    partial void OnPolicyChanged(ActivationPolicy value)
    {
        OnPropertyChanged(nameof(MaxDurationHours));
        OnPropertyChanged(nameof(RequiresJustification));
        OnPropertyChanged(nameof(RequiresTicket));
        OnPropertyChanged(nameof(ShowApprovalBanner));
        OnPropertyChanged(nameof(ShowAuthContextBanner));
    }

    partial void OnIsValidatingChanged(bool value) => OnPropertyChanged(nameof(IsBusy));

    partial void OnIsSubmittingChanged(bool value) => OnPropertyChanged(nameof(IsBusy));

    partial void OnJustificationChanged(string value)
    {
        OnPropertyChanged(nameof(CanSaveCurrentJustification));
        OnPropertyChanged(nameof(HasFavoritesOrSaveable));
    }

    /// <summary>
    /// The picker's one rule, from the other side: choosing the entire scope drops
    /// every tick — and with it a favourite that was still waiting for its list,
    /// which is the user saying they no longer want it. Unticking it leaves nothing
    /// selected, which is a legitimate state; Activate says so.
    /// </summary>
    partial void OnIsEntireScopeChanged(bool value)
    {
        if (_syncingSelection)
        {
            return;
        }

        if (value)
        {
            _pendingFavorite = null;
            _syncingSelection = true;
            try
            {
                foreach (var option in ScopeOptions)
                {
                    option.IsSelected = false;
                }
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        NotifyScopeSelectionChanged();
    }

    partial void OnScopeFilterTextChanged(string value) => ApplyScopeFilter();

    [RelayCommand]
    private Task ValidateAsync() => SubmitAsync(isValidationOnly: true);

    [RelayCommand]
    private Task SubmitAsync() => SubmitAsync(isValidationOnly: false);

    /// <summary>
    /// Back. Not while a batch is in flight — closing then would let a second batch
    /// start on top of the first; the footer's Cancel is gated the same way.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        if (IsBusy)
        {
            return;
        }

        IsOpen = false;
    }

    /// <summary>
    /// Chip click — drops the saved text into <see cref="Justification"/>,
    /// overwriting whatever the user had typed. Tooltip on the chip shows the
    /// full text so accidental overwrites are unlikely.
    /// </summary>
    [RelayCommand]
    private void ApplyFavorite(JustificationFavoriteViewModel? favorite)
    {
        if (favorite is null)
        {
            return;
        }

        Justification = favorite.FullText;
    }

    /// <summary>
    /// Chip × button — removes a saved favourite. Refreshes the chip row in
    /// place so the user sees the deletion immediately.
    /// </summary>
    [RelayCommand]
    private async Task DeleteFavoriteAsync(JustificationFavoriteViewModel? favorite)
    {
        if (favorite is null)
        {
            return;
        }

        await _favoritesStore.RemoveAsync(favorite.Favorite.Id);
        JustificationFavorites.Remove(favorite);
        OnPropertyChanged(nameof(CanSaveCurrentJustification));
        OnPropertyChanged(nameof(HasFavoritesOrSaveable));
    }

    /// <summary>
    /// Saves the current <see cref="Justification"/> as a new favourite for the
    /// current eligibility, then appends the chip to the row. Idempotent: if
    /// the text already exists for this role the store returns the existing
    /// entry and the chip row stays the same.
    /// </summary>
    [RelayCommand]
    private async Task SaveCurrentJustificationAsync()
    {
        if (Eligibility is not { } eligibility
            || Account is not { } account
            || string.IsNullOrWhiteSpace(Justification))
        {
            return;
        }

        var saved = await _favoritesStore.AddAsync(
            account.TenantId,
            eligibility.Kind,
            eligibility.ResourceId,
            eligibility.ScopeId,
            Justification.Trim());

        if (JustificationFavorites.All(f => f.Favorite.Id != saved.Id))
        {
            JustificationFavorites.Add(new JustificationFavoriteViewModel(saved));
        }

        OnPropertyChanged(nameof(CanSaveCurrentJustification));
        OnPropertyChanged(nameof(HasFavoritesOrSaveable));
    }

    /// <summary>Picker click. The list is read when the panel opens; this only retries a failed read.</summary>
    [RelayCommand]
    private void OpenScopePicker()
    {
        if (_scopeLoadFailed && Eligibility is { } eligibility && Account is { } account)
        {
            _scopeLoadFailed = false;
            _ = LoadScopesAsync(account, eligibility);
        }
    }

    /// <summary>Chip × under the picker — unticks that scope.</summary>
    [RelayCommand]
    private void RemoveScope(ScopeOptionViewModel? option)
    {
        if (option is not null)
        {
            option.IsSelected = false;
        }
    }

    /// <summary>
    /// Favourite click — the selection becomes exactly this set, replacing whatever
    /// was ticked. A saved set is a whole answer to "where do I need this role", so
    /// picking another one is a change of mind, not an addition. While the list is not
    /// there yet the favourite waits for it, and Activate refuses until it has been
    /// applied. Scopes that are no longer beneath the eligibility are named, not
    /// silently skipped.
    /// </summary>
    [RelayCommand]
    private void ApplyScopeFavorite(ScopeFavorite? favorite)
    {
        if (favorite is null)
        {
            return;
        }

        if (ScopeOptions.Count == 0)
        {
            _pendingFavorite = favorite;
            return;
        }

        var wanted = new HashSet<string>(favorite.Scopes.Select(scope => scope.Id), StringComparer.OrdinalIgnoreCase);
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Ticked in one go under the exclusivity rule's own guard, then the rule's
        // outcome applied once — a favourite of twenty scopes is not twenty relayouts.
        _syncingSelection = true;
        try
        {
            foreach (var option in ScopeOptions)
            {
                present.Add(option.Scope.Id);
                option.IsSelected = wanted.Contains(option.Scope.Id);
            }

            IsEntireScope = false;
        }
        finally
        {
            _syncingSelection = false;
        }

        NotifyScopeSelectionChanged();

        // Ticked rows must show through whatever is in the search box.
        ApplyScopeFilter();

        // Assigned either way: the selection is now this favourite's alone, so a
        // complaint about the previous one's missing scopes would be about nothing.
        var missing = favorite.Scopes.Where(scope => !present.Contains(scope.Id)).Select(scope => scope.Name).ToList();
        ScopeNotice = missing.Count > 0 ? $"No longer available: {string.Join(", ", missing)}." : null;
    }

    /// <summary>Saves the ticked scopes, under the typed name if any, and adds the chip.</summary>
    [RelayCommand]
    private async Task SaveScopeSelectionAsync()
    {
        if (Eligibility is not { } eligibility || Account is not { } account || !CanSaveScopeSelection)
        {
            return;
        }

        var fresh = new ScopeFavorite(
            Guid.NewGuid(),
            account.TenantId,
            eligibility.ResourceId,
            eligibility.ScopeId,
            SelectedScopeChips.Select(option => option.Scope).ToList(),
            DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(NewFavoriteName) ? null : NewFavoriteName.Trim(),
            account.ObjectId);

        if (!await SaveScopeFavoritesAsync(() => _scopeFavorites.AddAsync(fresh)))
        {
            return;
        }

        NewFavoriteName = string.Empty;
        ReloadScopeFavorites();
        NotifyScopeSelectionChanged();
    }

    /// <summary>
    /// Chip star — puts the set on the main page under PINNED, or takes it off again.
    /// Off keeps the favourite here in the panel; only the row on the main page goes.
    /// </summary>
    [RelayCommand]
    private async Task ToggleScopeFavoritePinAsync(ScopeFavorite? favorite)
    {
        if (favorite is null)
        {
            return;
        }

        if (await SaveScopeFavoritesAsync(() => _scopeFavorites.SetPinnedAsync(favorite.Id, !favorite.IsPinned)))
        {
            // Records are immutable, so the chips are rebuilt rather than mutated —
            // which is also what redraws the star.
            ReloadScopeFavorites();
        }
    }

    /// <summary>Chip × button — removes a saved scope favourite.</summary>
    [RelayCommand]
    private async Task DeleteScopeFavoriteAsync(ScopeFavorite? favorite)
    {
        if (favorite is null)
        {
            return;
        }

        if (!await SaveScopeFavoritesAsync(() => _scopeFavorites.RemoveAsync(favorite.Id)))
        {
            return;
        }

        ReloadScopeFavorites();
        NotifyScopeSelectionChanged();
    }

    /// <summary>
    /// The picker's one rule: an individual scope and the whole thing exclude each
    /// other. Unticking the last individual scope leaves the picker empty rather than
    /// falling back to the whole thing — that grant is never automatic.
    /// </summary>
    private void OnScopeSelectionChanged(ScopeOptionViewModel changed)
    {
        if (_syncingSelection)
        {
            return;
        }

        if (changed.IsSelected && IsEntireScope)
        {
            _syncingSelection = true;
            try
            {
                IsEntireScope = false;
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        NotifyScopeSelectionChanged();
    }

    /// <summary>Runs one store write; <c>false</c> when it failed.</summary>
    private async Task<bool> SaveScopeFavoritesAsync(Func<Task> write)
    {
        try
        {
            await write();
            return true;
        }
        catch (Exception ex)
        {
            // Same treatment the shell gives its own settings writes: logged, said out
            // loud, never an unhandled exception out of a command.
            _logger.LogWarning(ex, "Saving scope favourites failed");
            ScopeNotice = "The favourite could not be saved.";
            return false;
        }
    }

    /// <summary>
    /// Fetches favourites for the eligibility being activated and populates
    /// the chip row. Fire-and-forget from <see cref="Open"/>: a slow disk read
    /// (very rare on LocalAppData) must not block the slide-in animation.
    /// </summary>
    private async Task LoadFavoritesAsync(PimEligibility eligibility, SignedInAccount account)
    {
        var items = await _favoritesStore.GetForRoleAsync(
            account.TenantId,
            eligibility.Kind,
            eligibility.ResourceId,
            eligibility.ScopeId);

        JustificationFavorites.Clear();
        foreach (var item in items)
        {
            JustificationFavorites.Add(new JustificationFavoriteViewModel(item));
        }

        OnPropertyChanged(nameof(CanSaveCurrentJustification));
        OnPropertyChanged(nameof(HasFavoritesOrSaveable));
    }

    /// <summary>Fills the scope favourite chips for the open eligibility from the store.</summary>
    private void LoadScopeFavorites(SignedInAccount account, PimEligibility eligibility)
    {
        ScopeFavorites.Clear();
        foreach (var favorite in _scopeFavorites.ForRole(
            account.ObjectId, account.TenantId, eligibility.ResourceId, eligibility.ScopeId))
        {
            ScopeFavorites.Add(favorite);
        }
    }

    /// <summary>Re-reads the chips after a write, so they mirror the store rather than tracking it by hand.</summary>
    private void ReloadScopeFavorites()
    {
        if (Account is { } account && Eligibility is { } eligibility)
        {
            LoadScopeFavorites(account, eligibility);
        }
    }

    /// <summary>
    /// Fills the picker with the management groups and subscriptions beneath the
    /// eligibility's scope — from the cache when it was walked recently, else from ARM.
    /// </summary>
    private async Task LoadScopesAsync(SignedInAccount account, PimEligibility eligibility)
    {
        var generation = _generation;

        // Keyed by the enrollment, not just the tenant: two accounts in one tenant
        // rarely hold the same eligibilities, and serving one the other's child
        // scopes would offer scopes it cannot activate — and let a favourite save them.
        var cacheKey = $"{account.ObjectId}|{account.TenantId}|{eligibility.ScopeId}";
        IReadOnlyList<EligibleChildScope> scopes = [];
        string? notice = null;
        var failed = false;
        if (_scopeCache.TryGetValue(cacheKey, out var cached) && cached.ReadAt + ScopeCacheTtl > DateTimeOffset.UtcNow)
        {
            scopes = cached.Scopes;
        }
        else
        {
            IsLoadingScopes = true;
            try
            {
                using var cts = new CancellationTokenSource(ScopeWalkTimeout);
                scopes = await _aggregator.GetEligibleChildScopesAsync(account, eligibility.ScopeId, cts.Token);
                _scopeCache[cacheKey] = (DateTimeOffset.UtcNow, scopes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reading eligible child scopes failed (tenant {TenantId})", account.TenantId);
                notice = PimErrorMapper.Describe(PimErrorMapper.MapException(ex), account.AuthMethod);
                failed = true;
            }
        }

        // The panel may have been reopened while this was in flight — for another
        // role, or for the same one again; either way this read is not the open
        // panel's and must not land in its list.
        if (generation != _generation)
        {
            return;
        }

        IsLoadingScopes = false;
        _scopeLoadFailed = failed;
        foreach (var scope in scopes)
        {
            ScopeOptions.Add(new ScopeOptionViewModel(scope, OnScopeSelectionChanged));
        }

        if (!failed && scopes.Count == 0)
        {
            notice = "No management groups or subscriptions found beneath this scope.";
        }

        ScopeNotice = notice;

        // The user may have typed into the search box while the list was loading.
        ApplyScopeFilter();

        // A favourite that came with the panel, or was clicked while the list was
        // still on its way. It stays pending after a failed read, so the retry
        // applies it and Activate keeps refusing in the meantime.
        if (_pendingFavorite is { } pending && !failed)
        {
            _pendingFavorite = null;

            // A read that succeeded with nothing beneath the scope leaves no row to
            // tick, and ApplyScopeFavorite would put the favourite straight back into
            // pending — Activate would then refuse forever, with a picker retry that
            // cannot fire because the read never failed. Drop it: the notice above
            // says why the list is empty, and the entire-scope tick stays an
            // explicit decision the user has to make.
            if (ScopeOptions.Count > 0)
            {
                ApplyScopeFavorite(pending);
            }
        }
    }

    /// <summary>
    /// Hides rows the search box does not match. A ticked row always stays visible:
    /// what the Activate button is about to do must never sit behind a filter.
    /// </summary>
    private void ApplyScopeFilter()
    {
        var filter = ScopeFilterText.Trim();
        foreach (var option in ScopeOptions)
        {
            option.IsVisible = option.IsSelected || filter.Length == 0 || option.Matches(filter);
        }
    }

    /// <summary>Re-derives the chips and everything the picker's button and the save row show.</summary>
    private void NotifyScopeSelectionChanged()
    {
        ObservableCollectionSync.Apply(SelectedScopeChips, ScopeOptions.Where(option => option.IsSelected).ToList());
        OnPropertyChanged(nameof(SelectedScopeCount));
        OnPropertyChanged(nameof(ScopeSummary));
        OnPropertyChanged(nameof(HasNoScopeSelection));
        OnPropertyChanged(nameof(CanSaveScopeSelection));
        OnPropertyChanged(nameof(HasScopeFavoritesOrSaveable));
    }

    private async Task SubmitAsync(bool isValidationOnly)
    {
        if (Eligibility is not { } eligibility || Account is not { } account)
        {
            return;
        }

        var generation = _generation;
        ValidationMessage = null;
        ValidationSuccess = null;

        // A favourite that has not been applied must never fall through to the entire
        // scope: the row promised a narrowed activation, and the whole management
        // group is the opposite of that.
        if (_pendingFavorite is not null)
        {
            ValidationMessage = IsLoadingScopes
                ? "Still loading the scopes for this favourite — try again in a moment."
                : "The scopes for this favourite could not be loaded. Open the scope picker to try again.";
            return;
        }

        // Nothing preselected means nothing is granted by accident — and it means the
        // form is incomplete until the user says where.
        if (CanNarrowScope && HasNoScopeSelection)
        {
            ValidationMessage = "Please choose where to activate: one or more scopes, or the entire scope.";
            return;
        }

        if (RequiresJustification && string.IsNullOrWhiteSpace(Justification))
        {
            ValidationMessage = "Please provide a justification.";
            return;
        }

        // Only the number is mandatory — the ticketing rule does not require a
        // system, so demanding one would be our own hurdle, not Microsoft's.
        if (RequiresTicket && string.IsNullOrWhiteSpace(TicketNumber))
        {
            ValidationMessage = "Please provide a ticket number.";
            return;
        }

        // Narrowed: one request per ticked scope; null stands for the entire scope. A
        // dry-run never narrows — only the Graph surfaces have one, and they cannot.
        var narrowed = CanNarrowScope && !IsEntireScope && !isValidationOnly;
        List<ScopeOptionViewModel?> options = narrowed ? [.. SelectedScopeChips] : [null];

        var ticketSystem = string.IsNullOrWhiteSpace(TicketSystem) ? null : TicketSystem.Trim();
        var ticket = string.IsNullOrWhiteSpace(TicketNumber)
            ? null
            : new TicketInfo(TicketNumber.Trim(), ticketSystem);
        var justification = string.IsNullOrWhiteSpace(Justification) ? null : Justification.Trim();

        // Only a real activation stamps the auth-context claim — a dry-run must not
        // fling an MFA prompt at the user. The claim forces a token refresh
        // (MsalAuthService), and the token that yields is cached with the claim in
        // it, so only the first request of a narrowed batch carries it.
        var authContextClaim = !isValidationOnly && Policy.RequiresAuthContext
            ? Policy.AuthContextClaim
            : null;

        if (isValidationOnly)
        {
            IsValidating = true;
        }
        else
        {
            IsSubmitting = true;
        }

        var outcomes = new List<ActivationOutcome>();
        string? failure = null;
        try
        {
            // ponytail: sequential, stopping at the first failure — one step-up, one
            // message. Parallel with a cap if someone ticks dozens at a time.
            foreach (var option in options)
            {
                var request = new ActivationRequest(
                    eligibility,
                    TimeSpan.FromHours(DurationHours),
                    justification,
                    ticket,
                    isValidationOnly,
                    authContextClaim,
                    option?.Scope);

                ActivationResult result;
                try
                {
                    using var cts = new CancellationTokenSource(
                        request.AuthContextClaim is null ? GraphCallTimeout : StepUpTimeout);
                    result = await _aggregator.ActivateAsync(account, request, cts.Token);
                }
                catch (Exception ex)
                {
                    // Offline, timeout or an MSAL failure (e.g. cancelled WAM prompt) —
                    // the panel stays open with a friendly message and the user can retry.
                    _logger.LogWarning(
                        ex,
                        "Activation submit failed (tenant {TenantId}, validationOnly {IsValidationOnly})",
                        account.TenantId,
                        isValidationOnly);
                    result = new ActivationResult(string.Empty, ActivationStatus.Failed, null, null, PimErrorMapper.MapException(ex));
                }

                if (result.Error is { } error)
                {
                    var message = PimErrorMapper.Describe(error, account.AuthMethod);
                    failure = option is null ? message : $"{option.Scope.ScopeLabel}: {message}";
                    break;
                }

                if (isValidationOnly)
                {
                    // Dry-run succeeded; surface the inferred preconditions inline and
                    // keep the panel open so the user can submit for real.
                    ValidationSuccess = BuildValidationSuccessText(result);
                    return;
                }

                outcomes.Add(new ActivationOutcome(account, request, result));
                authContextClaim = null;

                // Unticked once granted: a retry after a later scope fails must not
                // request this one a second time.
                if (option is not null)
                {
                    _syncingSelection = true;
                    try
                    {
                        option.IsSelected = false;
                    }
                    finally
                    {
                        _syncingSelection = false;
                    }
                }
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsValidating = false;
                IsSubmitting = false;
                NotifyScopeSelectionChanged();
            }

            // What went through is reported right away, before the panel decides
            // whether to stay open — the shell's toast and pending rows do not wait
            // for the user to deal with a failed scope.
            if (outcomes.Count > 0)
            {
                Activated?.Invoke(outcomes);
            }
        }

        if (generation != _generation)
        {
            // Another slide-in closed the panel and it was reopened for something else
            // while this batch ran; the new session is left alone.
            return;
        }

        if (failure is not null)
        {
            // ANY error keeps the panel open with an inline message — a closing
            // panel plus a suppressible toast is how failures become invisible.
            // With several scopes the loop stops at the first failure: the scopes
            // before it are granted and unticked, the ones after it stay ticked.
            ValidationMessage = failure;
            return;
        }

        IsOpen = false;
    }

    private string BuildValidationSuccessText(ActivationResult result)
    {
        var parts = new List<string> { "Pre-check successful" };
        if (result.Status == ActivationStatus.PendingApproval || Policy.RequiresApproval)
        {
            parts.Add("approval required");
        }

        if (Policy.RequiresMfa || Policy.RequiresAuthContext)
        {
            parts.Add("additional verification (MFA) required");
        }

        return string.Join(" · ", parts) + ".";
    }
}
