namespace EntraPimManager.AppAvalonia.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.ErrorHandling;
using EntraPimManager.Core.Models;
using EntraPimManager.Core.Services;

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
/// </summary>
public sealed partial class ActivationPanelViewModel : ObservableObject
{
    /// <summary>Time budget for the activation / validation Graph call.</summary>
    private static readonly TimeSpan GraphCallTimeout = TimeSpan.FromSeconds(30);

    private readonly IEligibilityAggregator _aggregator;
    private readonly IJustificationFavoritesStore _favoritesStore;
    private readonly IUserSettingsService _userSettings;

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

    public ActivationPanelViewModel(
        IEligibilityAggregator aggregator,
        IJustificationFavoritesStore favoritesStore,
        IUserSettingsService userSettings)
    {
        _aggregator = aggregator;
        _favoritesStore = favoritesStore;
        _userSettings = userSettings;
    }

    /// <summary>Raised when the slide-in panel finishes — outcome is null on cancel.</summary>
    public event Action<ActivationResult?>? Closed;

    /// <summary>
    /// True while any submit is in flight. Disables every footer button so
    /// the user can't fire a second request before the first one returns.
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

    /// <summary>Prepares the panel for a new activation and slides it in.</summary>
    public void Open(SignedInAccount account, PimEligibility eligibility, ActivationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(eligibility);
        ArgumentNullException.ThrowIfNull(policy);

        Account = account;
        Eligibility = eligibility;
        Policy = policy;

        // Default duration comes from user settings, clamped to the role's
        // policy ceiling so the slider never starts above the maximum.
        var defaultDuration = _userSettings.Current.DefaultDurationHours;
        DurationHours = Math.Min(defaultDuration, policy.MaximumDuration.TotalHours);
        Justification = string.Empty;
        TicketNumber = string.Empty;
        TicketSystem = string.Empty;
        ValidationMessage = null;
        ValidationSuccess = null;
        IsValidating = false;
        IsSubmitting = false;
        JustificationFavorites.Clear();
        OnPropertyChanged(nameof(CanSaveCurrentJustification));
        OnPropertyChanged(nameof(HasFavoritesOrSaveable));
        IsOpen = true;

        _ = LoadFavoritesAsync(eligibility, account);
    }

    partial void OnIsOpenChanged(bool value) => OnPropertyChanged(nameof(PanelOffsetX));

    partial void OnAccountChanged(SignedInAccount? value) => OnPropertyChanged(nameof(AccountLabel));

    partial void OnEligibilityChanged(PimEligibility? value) => OnPropertyChanged(nameof(ResourceName));

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

    [RelayCommand]
    private Task ValidateAsync() => SubmitAsync(isValidationOnly: true);

    [RelayCommand]
    private Task SubmitAsync() => SubmitAsync(isValidationOnly: false);

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        Closed?.Invoke(null);
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

    private async Task SubmitAsync(bool isValidationOnly)
    {
        if (Eligibility is not { } eligibility || Account is not { } account)
        {
            return;
        }

        ValidationMessage = null;
        ValidationSuccess = null;

        if (RequiresJustification && string.IsNullOrWhiteSpace(Justification))
        {
            ValidationMessage = "Please provide a justification.";
            return;
        }

        if (RequiresTicket && (string.IsNullOrWhiteSpace(TicketNumber) || string.IsNullOrWhiteSpace(TicketSystem)))
        {
            ValidationMessage = "Please provide a ticket number and ticket system.";
            return;
        }

        var ticket = string.IsNullOrWhiteSpace(TicketNumber)
            ? null
            : new TicketInfo(TicketNumber.Trim(), TicketSystem.Trim());
        var request = new ActivationRequest(
            eligibility,
            TimeSpan.FromHours(DurationHours),
            string.IsNullOrWhiteSpace(Justification) ? null : Justification.Trim(),
            ticket,
            isValidationOnly);

        if (isValidationOnly)
        {
            IsValidating = true;
        }
        else
        {
            IsSubmitting = true;
        }

        ActivationResult result;
        try
        {
            using var cts = new CancellationTokenSource(GraphCallTimeout);
            result = await _aggregator.ActivateAsync(account, request, cts.Token);
        }
        catch (Exception ex)
        {
            // Offline or timeout — the service layer only maps ODataError, so
            // the panel stays open with a friendly message and the user can retry.
            ValidationMessage = PimErrorMapper.MapException(ex).Message;
            return;
        }
        finally
        {
            IsValidating = false;
            IsSubmitting = false;
        }

        if (result.Error is { Severity: ErrorSeverity.Validation } validationError)
        {
            // Keep the panel open so the user can correct the input.
            ValidationMessage = validationError.Message;
            return;
        }

        if (isValidationOnly)
        {
            // Dry-run succeeded; surface the inferred preconditions inline and
            // keep the panel open so the user can submit for real.
            ValidationSuccess = BuildValidationSuccessText(result);
            return;
        }

        IsOpen = false;
        Closed?.Invoke(result);
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
