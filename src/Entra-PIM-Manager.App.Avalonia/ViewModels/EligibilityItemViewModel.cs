namespace EntraPimManager.AppAvalonia.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Models;

/// <summary>A single row in the eligibility list.</summary>
public sealed partial class EligibilityItemViewModel : ObservableObject
{
    private readonly Func<EligibilityItemViewModel, Task> _activate;

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

    public EligibilityItemViewModel(
        PimEligibility eligibility,
        SignedInAccount account,
        Func<EligibilityItemViewModel, Task> activate)
    {
        Eligibility = eligibility;
        Account = account;
        _activate = activate;
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

    /// <summary>Localized label for the resource kind.</summary>
    public string KindLabel => Eligibility.Kind switch
    {
        PimResourceKind.DirectoryRole => "Directory role",
        PimResourceKind.GroupMembership => "Group membership",
        PimResourceKind.GroupOwnership => "Group ownership",
        _ => string.Empty,
    };

    /// <summary>Whether to show the role-assignable-group warning badge.</summary>
    public bool ShowRoleAssignableWarning => Eligibility.IsRoleAssignableGroup;

    /// <summary>
    /// True when this eligibility is not currently active and not in the
    /// middle of opening its activation panel. Bound to the row's
    /// <c>IsHitTestVisible</c> so neither an active nor an in-flight row
    /// can fire a second activation.
    /// </summary>
    public bool CanActivate => !IsCurrentlyActive && !IsActivating;

    /// <summary>Opacity for the row labels — dimmed when the eligibility is active or activating.</summary>
    public double LabelOpacity => IsCurrentlyActive || IsActivating ? 0.55 : 1.0;

    [RelayCommand]
    private Task ActivateAsync() => CanActivate ? _activate(this) : Task.CompletedTask;

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
