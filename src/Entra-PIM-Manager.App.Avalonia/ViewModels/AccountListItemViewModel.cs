namespace EntraPimManager.AppAvalonia.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly Action<AccountListItemViewModel, string?> _rename;

    [ObservableProperty]
    private string? _tenantName;

    /// <summary>
    /// Short self-chosen name for this enrollment, or <c>null</c> when the user
    /// has not set one. Owned by the shell (it persists the value); the row only
    /// displays it and hands edits back through the rename callback.
    /// </summary>
    [ObservableProperty]
    private string? _accountAlias;

    /// <summary>True while the row's name line is swapped for the alias text box.</summary>
    [ObservableProperty]
    private bool _isRenaming;

    /// <summary>Text-box content while renaming. Committed or discarded, never read otherwise.</summary>
    [ObservableProperty]
    private string _aliasDraft = string.Empty;

    public AccountListItemViewModel(
        SignedInAccount account,
        Action<AccountListItemViewModel, string?> rename,
        IRelayCommand<SignedInAccount?> select,
        IAsyncRelayCommand<SignedInAccount?> remove)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(rename);
        ArgumentNullException.ThrowIfNull(select);
        ArgumentNullException.ThrowIfNull(remove);
        Account = account;
        _rename = rename;
        SelectCommand = select;
        RemoveCommand = remove;
    }

    /// <summary>The underlying account — passed as the parameter of the two commands below.</summary>
    public SignedInAccount Account { get; }

    /// <summary>
    /// Makes this enrollment the active context. The shell's own command instance, held
    /// here so the row template binds against its own data context.
    /// </summary>
    /// <remarks>
    /// The row used to reach the shell by walking up to the enclosing items control. That
    /// only worked while the account list was a flat top-level list — once rows sit inside
    /// a tenant node, the walk finds the inner control instead and silently binds to the
    /// wrong thing. Carrying the command is what the rest of the codebase does anyway.
    /// </remarks>
    public IRelayCommand<SignedInAccount?> SelectCommand { get; }

    /// <summary>Removes this enrollment. See <see cref="SelectCommand"/> for why it lives here.</summary>
    public IAsyncRelayCommand<SignedInAccount?> RemoveCommand { get; }

    /// <summary>
    /// Name line of the row (and the source of the avatar initials): the alias
    /// when set, otherwise the identity's Entra display name, otherwise the UPN.
    /// The UPN keeps its own line below, so this row never hides which account
    /// it really is — the alias is a convenience, not a substitute.
    /// </summary>
    public string DisplayName => AccountAlias ?? Account.DisplayName ?? Account.Username;

    /// <summary>UPN / login of the enrolled identity (UI text).</summary>
    public string Username => Account.Username;

    /// <summary>Tenant id (GUID) of this enrollment.</summary>
    public string TenantId => Account.TenantId;

    /// <summary>
    /// Composed tenant label: resolved name + tid GUID when known, GUID alone
    /// otherwise. Keeps the row readable while the async name fetch is in flight.
    /// </summary>
    public string TenantLabel => TenantLabelFormatter.Format(TenantName, Account.TenantId, includeId: true);

    /// <summary>
    /// True for enrollments signed in via the device-code fallback. Surfaced as a
    /// row badge because the method is fixed at enrollment time and silently
    /// limits what the account can do — most visibly, it cannot satisfy a
    /// Conditional Access authentication context on role activation.
    /// </summary>
    public bool IsDeviceCodeAccount => Account.AuthMethod == AuthMethod.DeviceCode;

    [RelayCommand]
    private void BeginRename()
    {
        AliasDraft = AccountAlias ?? string.Empty;
        IsRenaming = true;
    }

    [RelayCommand]
    private void CommitRename()
    {
        var trimmed = AliasDraft.Trim();
        _rename(this, trimmed.Length == 0 ? null : trimmed);
        IsRenaming = false;
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;

    partial void OnTenantNameChanged(string? value) => OnPropertyChanged(nameof(TenantLabel));

    partial void OnAccountAliasChanged(string? value) => OnPropertyChanged(nameof(DisplayName));
}
