namespace EntraPimManager.AppAvalonia.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using EntraPimManager.Core.Auth;

/// <summary>
/// Account-management surface exposed by <see cref="ShellViewModel"/> and
/// consumed by <see cref="SettingsPanelViewModel"/>'s ACCOUNTS section.
/// </summary>
/// <remarks>
/// Settings used to be a leaf view model that only depended on user-settings
/// storage. Moving the account-management UI into Settings would create a
/// constructor-level cycle (Shell needs Settings; Settings would then need
/// Shell). The host interface plus late-binding via
/// <see cref="SettingsPanelViewModel.AttachAccountsHost"/> breaks the cycle:
/// DI builds Settings first without a host, then Shell, and Shell attaches
/// itself in its constructor.
/// </remarks>
public interface IAccountsHost
{
    /// <summary>Enrolled accounts in stable order, wrapped for the row template.</summary>
    ObservableCollection<AccountListItemViewModel> Accounts { get; }

    /// <summary>Removes the passed account.</summary>
    IAsyncRelayCommand<SignedInAccount?> RemoveAccountCommand { get; }

    /// <summary>Opens the single "Add account" slide-in (broker primary, device
    /// code under Advanced; tenant field optional).</summary>
    IRelayCommand OpenAddAccountPanelCommand { get; }

    /// <summary>Selects the passed account as the active context.</summary>
    IRelayCommand<SignedInAccount?> SelectAccountCommand { get; }
}
