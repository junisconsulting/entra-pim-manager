namespace EntraPimManager.AppAvalonia.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using EntraPimManager.Core.Auth;

/// <summary>
/// Wraps a <see cref="SignedInAccount"/> for the account-switcher popout. Carries
/// a mutable <see cref="TenantName"/> so the row can update once the per-account
/// tenant name is resolved asynchronously.
/// </summary>
/// <remarks>
/// Two enrollments of the same identity in different tenants share oid, username
/// and display name — the tenant label is the only differentiator the user sees,
/// so the popout binds to <see cref="TenantLabel"/> as a stable composed string.
/// </remarks>
public sealed partial class AccountListItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string? _tenantName;

    public AccountListItemViewModel(SignedInAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        Account = account;
    }

    /// <summary>The underlying account — bound by the SelectAccount / RemoveAccount commands.</summary>
    public SignedInAccount Account { get; }

    /// <summary>Display name of the enrolled identity (UI text).</summary>
    public string DisplayName => Account.DisplayName ?? Account.Username;

    /// <summary>UPN / login of the enrolled identity (UI text).</summary>
    public string Username => Account.Username;

    /// <summary>Tenant id (GUID) of this enrollment.</summary>
    public string TenantId => Account.TenantId;

    /// <summary>
    /// Composed tenant label: resolved name + tid GUID when known, GUID alone
    /// otherwise. Keeps the row readable while the async name fetch is in flight.
    /// </summary>
    public string TenantLabel => TenantLabelFormatter.Format(TenantName, Account.TenantId, includeId: true);

    partial void OnTenantNameChanged(string? value) => OnPropertyChanged(nameof(TenantLabel));
}
