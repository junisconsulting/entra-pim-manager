namespace EntraPimManager.AppAvalonia.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EntraPimManager.Core.Auth;

/// <summary>
/// One collapsible group in the ELIGIBILITIES list — represents the
/// eligibilities of a single enrolled account. Phase 3 globalises the
/// list across tenants; the group lets the user keep each tenant's rows
/// folded away until needed.
/// </summary>
/// <remarks>
/// IsExpanded changes flow back to <see cref="ShellViewModel"/> via the
/// <see cref="ExpansionToggledByUser"/> event so Phase 4 can persist the
/// state. The shell sets <see cref="SuppressUserExpansionEvent"/> while
/// applying filter-driven expansions so a search doesn't poison the
/// persisted layout.
/// </remarks>
public sealed partial class TenantEligibilityGroup : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private int _matchCount;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private string? _tenantName;

    /// <summary>
    /// Short self-chosen name for this enrollment, pushed in by the shell.
    /// <c>null</c> when the user has set no alias — the header then shows the UPN.
    /// </summary>
    [ObservableProperty]
    private string? _accountAlias;

    /// <summary>
    /// Why the last eligibility fetch for this account failed, in user-facing
    /// words — <c>null</c> when it succeeded. Rendered as a warning row under
    /// the header so an unlicensed tenant doesn't masquerade as a bare "(0)".
    /// </summary>
    [ObservableProperty]
    private string? _loadError;

    /// <summary>
    /// How many of this tenant's eligibilities are active right now. Groups are
    /// collapsed by default, so without this the header of a tenant holding an
    /// active role is indistinguishable from one holding none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActive))]
    [NotifyPropertyChangedFor(nameof(ActiveLabel))]
    private int _activeCount;

    public TenantEligibilityGroup(SignedInAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        Account = account;
    }

    /// <summary>Raised when the user clicks the header chevron to expand or collapse.</summary>
    public event Action<bool>? ExpansionToggledByUser;

    /// <summary>Account the group belongs to — drives header text and routing.</summary>
    public SignedInAccount Account { get; }

    /// <summary>
    /// Every eligibility of this enrollment, flat. The canonical list: filtering,
    /// the active-marking pass and the counts all work on it, while the sections
    /// below only decide how the same objects are laid out.
    /// </summary>
    public ObservableCollection<EligibilityItemViewModel> Items { get; } = [];

    /// <summary>
    /// The kind sections this tenant renders — directory roles, administrative units,
    /// groups, Azure resources — in that order, and only those that hold something.
    /// </summary>
    public ObservableCollection<EligibilitySection> Sections { get; } = [];

    /// <summary>Every Azure role node in this tenant, across its sections.</summary>
    public IEnumerable<AzureRoleGroup> RoleGroups => Sections.SelectMany(section => section.RoleGroups);

    /// <summary>Username (UPN) shown in the header.</summary>
    public string Username => Account.Username;

    /// <summary>
    /// What the header actually renders on its second line: the alias when set,
    /// the UPN otherwise. The UPN is the fallback rather than the Entra display
    /// name because it is what distinguishes two enrollments of one person, and
    /// the full value stays reachable as the line's tooltip.
    /// </summary>
    public string AliasOrUpn => AccountAlias ?? Username;

    /// <summary>Tenant id (GUID) of the enrollment.</summary>
    public string TenantId => Account.TenantId;

    /// <summary>Composed header label: <c>"{TenantName}"</c> when resolved, GUID fallback otherwise.</summary>
    public string TenantLabel => TenantLabelFormatter.Format(TenantName, TenantId);

    /// <summary>True while at least one eligibility in this tenant is active.</summary>
    public bool HasActive => ActiveCount > 0;

    /// <summary>Badge text for the active entries, e.g. <c>"2 active"</c>.</summary>
    public string ActiveLabel => $"{ActiveCount} active";

    /// <summary>
    /// When true, an <see cref="IsExpanded"/> change does NOT raise
    /// <see cref="ExpansionToggledByUser"/>. Set by the shell while it
    /// applies filter-driven or programmatic expand/collapse so persistence
    /// (Phase 4) only fires on real user toggles.
    /// </summary>
    public bool SuppressUserExpansionEvent { get; set; }

    partial void OnTenantNameChanged(string? value) => OnPropertyChanged(nameof(TenantLabel));

    partial void OnAccountAliasChanged(string? value) => OnPropertyChanged(nameof(AliasOrUpn));

    partial void OnIsExpandedChanged(bool value)
    {
        if (SuppressUserExpansionEvent)
        {
            return;
        }

        ExpansionToggledByUser?.Invoke(value);
    }

    [RelayCommand]
    private void ToggleExpansion() => IsExpanded = !IsExpanded;
}
