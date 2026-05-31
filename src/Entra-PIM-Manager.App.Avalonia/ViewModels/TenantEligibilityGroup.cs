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

    public TenantEligibilityGroup(SignedInAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        Account = account;
    }

    /// <summary>Raised when the user clicks the header chevron to expand or collapse.</summary>
    public event Action<bool>? ExpansionToggledByUser;

    /// <summary>Account the group belongs to — drives header text and routing.</summary>
    public SignedInAccount Account { get; }

    /// <summary>Eligibility rows shown when the group is expanded.</summary>
    public ObservableCollection<EligibilityItemViewModel> Items { get; } = [];

    /// <summary>Username (UPN) shown in the header.</summary>
    public string Username => Account.Username;

    /// <summary>Tenant id (GUID) of the enrollment.</summary>
    public string TenantId => Account.TenantId;

    /// <summary>Composed header label: <c>"{TenantName}"</c> when resolved, GUID fallback otherwise.</summary>
    public string TenantLabel => TenantLabelFormatter.Format(TenantName, TenantId);

    /// <summary>
    /// When true, an <see cref="IsExpanded"/> change does NOT raise
    /// <see cref="ExpansionToggledByUser"/>. Set by the shell while it
    /// applies filter-driven or programmatic expand/collapse so persistence
    /// (Phase 4) only fires on real user toggles.
    /// </summary>
    public bool SuppressUserExpansionEvent { get; set; }

    partial void OnTenantNameChanged(string? value) => OnPropertyChanged(nameof(TenantLabel));

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
