namespace EntraPimManager.AppAvalonia.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using EntraPimManager.Core.Configuration;

/// <summary>
/// Account-management surface exposed by <see cref="ShellViewModel"/> and
/// consumed by <see cref="SettingsPanelViewModel"/>'s TENANTS section.
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
    /// <summary>
    /// Enrolled accounts in stable order, wrapped for the row template. The tenant tree
    /// groups these very instances — it never copies them, because the shell pushes
    /// resolved tenant names and alias edits into the rows it reaches through here.
    /// </summary>
    ObservableCollection<AccountListItemViewModel> Accounts { get; }

    /// <summary>
    /// Opens the "Add account" slide-in for one tenant (broker primary, device code
    /// under Advanced). The tenant comes from the card the button sits in, so the
    /// slide-in has nothing left to ask.
    /// </summary>
    IRelayCommand<TenantSlot?> OpenAddAccountPanelCommand { get; }
}
