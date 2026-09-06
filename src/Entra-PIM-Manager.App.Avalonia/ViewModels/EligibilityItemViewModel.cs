namespace EntraPimManager.AppAvalonia.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Models;

/// <summary>A single row in the eligibility list.</summary>
public sealed partial class EligibilityItemViewModel : ObservableObject
{
    private readonly Func<EligibilityItemViewModel, Task> _activate;
    private readonly Action<EligibilityItemViewModel> _togglePin;

    /// <summary>
    /// True when an active assignment for the same (Kind, ResourceId, ScopeId)
    /// exists for the current account. Drives the dimmed-row + "● Aktiv" badge
    /// and disables the activation path so the user can't double-activate.
    /// </summary>
    [ObservableProperty]
    private bool _isCurrentlyActive;

    /// <summary>
    /// True while the shell is loading the activation policy (cold cache) and
    /// preparing to open the activation panel. Drives a spinner on the row so
    /// the click feels acknowledged even when the policy fetch is slow.
    /// </summary>
    [ObservableProperty]
    private bool _isActivating;

    /// <summary>
    /// Tenant display name (e.g. <c>"junis"</c>), filled asynchronously by the
    /// shell once <c>/organization</c> resolves. <c>null</c> while in flight.
    /// </summary>
    [ObservableProperty]
    private string? _tenantName;

    /// <summary>
    /// Inline error caption shown under the row when the click could not open
    /// the activation panel. Without it the row's only feedback is a Windows
    /// toast, which Focus Assist / DND swallows — the click then looks ignored.
    /// Mirrors <see cref="ActiveAssignmentItemViewModel.DeactivationErrorText"/>.
    /// Cleared at the start of the next attempt.
    /// </summary>
    [ObservableProperty]
    private string? _activationErrorText;

    /// <summary>
    /// False while a search filter is active and this row does not match it.
    /// Filtering hides rows rather than rebuilding the list so the row objects —
    /// and with them the active-marking and any inline error — survive a search.
    /// </summary>
    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>True while this eligibility is pinned to the top of the list.</summary>
    [ObservableProperty]
    private bool _isPinned;

    public EligibilityItemViewModel(
        PimEligibility eligibility,
        SignedInAccount account,
        Func<EligibilityItemViewModel, Task> activate,
        Action<EligibilityItemViewModel> togglePin)
    {
        Eligibility = eligibility;
        Account = account;
        _activate = activate;
        _togglePin = togglePin;
    }

    /// <summary>The underlying eligibility.</summary>
    public PimEligibility Eligibility { get; }

    /// <summary>The account this eligibility belongs to — needed to route the activation call to the correct tenant.</summary>
    public SignedInAccount Account { get; }

    /// <summary>Tenant GUID of the enrollment this eligibility belongs to.</summary>
    public string TenantId => Account.TenantId;

    /// <summary>
    /// Composed tenant label: <c>"{TenantName}"</c> when resolved, GUID fallback
    /// otherwise. Mirrors <see cref="ActiveAssignmentItemViewModel.TenantLabel"/>.
    /// </summary>
    public string TenantLabel => TenantLabelFormatter.Format(TenantName, TenantId);

    /// <summary>Display name of the role or group.</summary>
    public string DisplayName => Eligibility.DisplayName;

    /// <summary>
    /// True when this row sits under an <see cref="AzureRoleGroup"/> node. Set once
    /// while the list is built; the row then leads with its scope, because the role
    /// name is already the heading right above it.
    /// </summary>
    public bool IsInRoleGroup { get; init; }

    /// <summary>
    /// True for the copies rendered in the pinned / recent sections at the top of the
    /// list. They stand outside their tenant group, so they carry that context on a
    /// line of their own instead of the kind and scope captions.
    /// </summary>
    public bool IsShortcut { get; init; }

    /// <summary>Heading of the row: the scope inside a role node, the role name everywhere else.</summary>
    public string PrimaryLabel => IsInRoleGroup ? ScopeLabel ?? DisplayName : DisplayName;

    /// <summary>
    /// The kind caption is redundant under a role node — every child has the same kind —
    /// and under a section header, which already names the kind. It survives for groups,
    /// where it is the only thing separating membership from ownership.
    /// </summary>
    public bool ShowKindLine => !IsInRoleGroup && !IsShortcut
        && Eligibility.Kind is PimResourceKind.GroupMembership or PimResourceKind.GroupOwnership;

    /// <summary>The separate scope line is only needed where the heading is the role name.</summary>
    public bool ShowScopeLine => !IsInRoleGroup && !IsShortcut && !string.IsNullOrEmpty(ScopeLabel);

    /// <summary>
    /// Tenant plus scope (or kind) for a shortcut row — without it three identical
    /// "Owner" rows would sit at the top of the list with nothing to tell them apart.
    /// </summary>
    public string? ContextLine => IsShortcut
        ? string.Join(" · ", new[] { TenantLabel, ScopeLabel ?? KindLabel }.Where(part => !string.IsNullOrEmpty(part)))
        : null;

    /// <summary>Localized label for the resource kind.</summary>
    public string KindLabel => Eligibility.Kind switch
    {
        PimResourceKind.DirectoryRole => "Directory role",
        PimResourceKind.GroupMembership => "Group membership",
        PimResourceKind.GroupOwnership => "Group ownership",
        PimResourceKind.AzureResourceRole => "Azure resource role",
        _ => string.Empty,
    };

    /// <summary>
    /// Scope line: the ARM scope for Azure resource roles ("Subscription: Prod"), the
    /// administrative unit for a directory role confined to one, <c>null</c> otherwise.
    /// </summary>
    /// <remarks>
    /// The administrative unit is named by its object id, not its display name — resolving
    /// that needs a directory read the app does not ask consent for. An id is still the
    /// difference between "User Administrator" and "User Administrator, but only there".
    /// </remarks>
    public string? ScopeLabel => Eligibility.ScopeLabel
        ?? (Eligibility.AdministrativeUnitId is { } unitId ? $"Administrative unit: {unitId}" : null)
        ?? (Eligibility.NarrowedScopeId is { } scopeId ? $"Scope: {scopeId}" : null);

    /// <summary>
    /// True when this eligibility is not currently active and not in the
    /// middle of opening its activation panel. Bound to the row's
    /// <c>IsHitTestVisible</c> so neither an active nor an in-flight row
    /// can fire a second activation.
    /// </summary>
    public bool CanActivate => !IsCurrentlyActive && !IsActivating;

    /// <summary>Opacity for the row labels — dimmed when the eligibility is active or activating.</summary>
    public double LabelOpacity => IsCurrentlyActive || IsActivating ? 0.55 : 1.0;

    /// <summary>
    /// Whether the row matches the search box. Compares everything the row shows:
    /// role name, kind, tenant and — for Azure resource roles — the scope. With a
    /// PIM for Azure Resources assignment on every subscription of a large estate,
    /// the subscription name is the only practical way to find a row, and the role
    /// name alone would match hundreds of them.
    /// </summary>
    public bool Matches(string filter)
        => DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || KindLabel.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || TenantLabel.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || (ScopeLabel?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);

    [RelayCommand]
    private Task ActivateAsync() => CanActivate ? _activate(this) : Task.CompletedTask;

    [RelayCommand]
    private void TogglePin() => _togglePin(this);

    partial void OnIsCurrentlyActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(CanActivate));
        OnPropertyChanged(nameof(LabelOpacity));
    }

    partial void OnIsActivatingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanActivate));
        OnPropertyChanged(nameof(LabelOpacity));
    }

    partial void OnTenantNameChanged(string? value) => OnPropertyChanged(nameof(TenantLabel));
}
