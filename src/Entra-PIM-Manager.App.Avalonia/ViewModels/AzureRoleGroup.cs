namespace EntraPimManager.AppAvalonia.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

/// <summary>
/// One Azure resource role inside a tenant group, holding every scope the account
/// is eligible for it on. Collapsed by default: an infrastructure team member can
/// be eligible for the same role on hundreds of subscriptions, and a flat list of
/// those is unreadable and slow to build.
/// </summary>
/// <remarks>
/// Only created when a role appears on **two or more** scopes — a single-scope role
/// stays a plain row, because a node that hides one child costs a click and shows
/// nothing. The children are the very same row view models the tenant group holds
/// in its canonical <see cref="TenantEligibilityGroup.Items"/> list, so filtering
/// and the active-marking pass keep working on one set of objects.
/// </remarks>
public sealed partial class AzureRoleGroup : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private int _matchCount;

    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>
    /// How many of the scopes behind this node are active right now. Surfaced on the
    /// collapsed node: hiding 400 scopes must not also hide the fact that one of them
    /// is currently granting Owner.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActive))]
    [NotifyPropertyChangedFor(nameof(ActiveLabel))]
    private int _activeCount;

    public AzureRoleGroup(string roleDefinitionId, string displayName)
    {
        RoleDefinitionId = roleDefinitionId;
        DisplayName = displayName;
    }

    /// <summary>ARM role definition id — the identity this node groups by.</summary>
    public string RoleDefinitionId { get; }

    /// <summary>Role name shown on the node ("Owner").</summary>
    public string DisplayName { get; }

    /// <summary>The scope rows behind this role.</summary>
    public ObservableCollection<EligibilityItemViewModel> Items { get; } = [];

    /// <summary>Caption on the right of the node: how many scopes it stands for.</summary>
    public string ScopeCountLabel => MatchCount == 1 ? "1 scope" : $"{MatchCount} scopes";

    /// <summary>True while at least one scope behind this node is active.</summary>
    public bool HasActive => ActiveCount > 0;

    /// <summary>Badge text for the active scopes, e.g. <c>"2 active"</c>.</summary>
    public string ActiveLabel => $"{ActiveCount} active";

    [RelayCommand]
    private void ToggleExpansion() => IsExpanded = !IsExpanded;

    partial void OnMatchCountChanged(int value) => OnPropertyChanged(nameof(ScopeCountLabel));
}
