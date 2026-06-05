namespace EntraPimManager.AppAvalonia.ViewModels;

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EntraPimManager.AppAvalonia.Services;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;
using EntraPimManager.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// View model for the Settings slide-in panel. Mirrors the activation /
/// add-tenant pattern: an animated overlay with auto-save on each control
/// change (no explicit Save button), exposing a <see cref="Closed"/> event so
/// the shell can keep the panel slots mutually exclusive.
/// </summary>
public sealed partial class SettingsPanelViewModel : ObservableObject
{
    private readonly IUserSettingsService _userSettings;
    private readonly IAutostartService _autostart;
    private readonly IShortcutService _shortcuts;
    private readonly EntraPimManagerOptions _options;
    private readonly ILogger<SettingsPanelViewModel> _logger;

    /// <summary>
    /// When true, the <c>OnXxxChanged</c> partial-void handlers skip
    /// <see cref="PersistAndApply"/>. Used while <see cref="Open"/> seeds the
    /// observable properties from the current settings so the act of opening
    /// the panel doesn't immediately rewrite the file.
    /// </summary>
    private bool _suppressPersist;

    /// <summary>
    /// The accounts host (Shell) is attached after construction via
    /// <see cref="AttachAccountsHost"/> — see <see cref="IAccountsHost"/> for
    /// why this isn't a constructor dependency.
    /// </summary>
    private IAccountsHost? _accountsHost;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private ThemeOption _selectedTheme;

    [ObservableProperty]
    private bool _startWithWindows;

    /// <summary>
    /// Bound to the "Start menu entry" toggle. Like <see cref="StartWithWindows"/>
    /// the artifact itself (the shortcut file) is the source of truth, not the
    /// persisted settings — so toggling create/removes it directly and never calls
    /// <c>SaveAsync</c>. The row is hidden when <see cref="CanManageStartMenuShortcut"/>
    /// is false (dev / portable, where there is no install to anchor a shortcut to).
    /// </summary>
    [ObservableProperty]
    private bool _createStartMenuShortcut;

    [ObservableProperty]
    private DurationOption _selectedDuration;

    [ObservableProperty]
    private bool _expiryWarningEnabled;

    [ObservableProperty]
    private ExpiryThresholdOption _selectedExpiryThreshold;

    [ObservableProperty]
    private bool _automaticUpdatesEnabled;

    /// <summary>
    /// Whether the ACCOUNTS section is expanded. Persisted in
    /// <see cref="UserSettings.SettingsAccountsExpanded"/>; defaults to
    /// expanded so first-run users see the section is there.
    /// </summary>
    [ObservableProperty]
    private bool _isAccountsSectionExpanded = true;

    /// <summary>
    /// Bound to the App Registration ClientId TextBox. Validated as a GUID
    /// before <see cref="SaveClientIdCommand"/> writes it to the local config
    /// file. Seeded from the current effective value when the panel opens.
    /// </summary>
    [ObservableProperty]
    private string _clientIdInput = string.Empty;

    /// <summary>
    /// True after the user successfully saved a new ClientId. Drives the
    /// inline "Restart required" banner — the new value only takes effect
    /// on the next process start.
    /// </summary>
    [ObservableProperty]
    private bool _showRestartPrompt;

    public SettingsPanelViewModel(
        IUserSettingsService userSettings,
        IAutostartService autostart,
        IShortcutService shortcuts,
        IOptions<EntraPimManagerOptions> options,
        ILogger<SettingsPanelViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _userSettings = userSettings;
        _autostart = autostart;
        _shortcuts = shortcuts;
        _options = options.Value;
        _logger = logger;

        _selectedTheme = ThemeOptions[0];
        _selectedDuration = DurationOptions[0];
        _selectedExpiryThreshold = ExpiryThresholdOptions[0];
    }

    /// <summary>Raised when the panel closes — payload-less; the shell uses it to drop the exclusive-toggle.</summary>
    public event Action? Closed;

    /// <summary>
    /// Raised by the "Check for updates" button. <see cref="Tray.UpdateController"/>
    /// subscribes to it; the VM has no direct dependency on the updater.
    /// </summary>
    public event Func<Task>? CheckForUpdatesRequested;

    /// <summary>Theme choices presented in the ComboBox.</summary>
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } = new[]
    {
        new ThemeOption(ThemePreference.System, "System"),
        new ThemeOption(ThemePreference.Light, "Light"),
        new ThemeOption(ThemePreference.Dark, "Dark"),
    };

    /// <summary>Default-duration choices presented in the ComboBox.</summary>
    public IReadOnlyList<DurationOption> DurationOptions { get; } = new[]
    {
        new DurationOption(1.0, "1 hour"),
        new DurationOption(2.0, "2 hours"),
        new DurationOption(4.0, "4 hours"),
        new DurationOption(8.0, "8 hours"),
    };

    /// <summary>Expiry-warning threshold choices presented in the ComboBox.</summary>
    public IReadOnlyList<ExpiryThresholdOption> ExpiryThresholdOptions { get; } = new[]
    {
        new ExpiryThresholdOption(5, "5 minutes"),
        new ExpiryThresholdOption(10, "10 minutes"),
        new ExpiryThresholdOption(15, "15 minutes"),
    };

    /// <summary>X-offset for the slide-in transform — mirrors the other panels.</summary>
    public double PanelOffsetX => IsOpen ? 0 : 420;

    /// <summary>
    /// Whether the "Start menu entry" row is shown. False outside a real Velopack
    /// install (dev / portable), where there is no shortcut to manage — the row
    /// then hides rather than presenting a toggle that does nothing.
    /// </summary>
    public bool CanManageStartMenuShortcut => _shortcuts.IsSupported;

    /// <summary>
    /// True when <see cref="ClientIdInput"/> contains a syntactically valid
    /// GUID. Drives the enabled state of the Save button so the user gets
    /// inline feedback before submission.
    /// </summary>
    public bool IsClientIdInputValid => Guid.TryParse(ClientIdInput, out _);

    /// <summary>
    /// True when the currently active configuration has no usable ClientId —
    /// either empty or a non-GUID placeholder. Drives the inline "not yet
    /// configured" hint inside the App Registration section.
    /// </summary>
    public bool IsCurrentClientIdMissing =>
        string.IsNullOrWhiteSpace(_options.ClientId)
        || !Guid.TryParse(_options.ClientId, out _);

    /// <summary>
    /// Folder holding the rolling Serilog files — the same location
    /// <c>App.BuildHost</c> configures the file sink to write to.
    /// </summary>
    public string LogDirectory => AppPaths.LogDirectory;

    /// <summary>Enrolled accounts surfaced into the ACCOUNTS section. Empty
    /// when the host isn't attached yet (test scenarios) — the binding
    /// degrades gracefully to an empty list.</summary>
    public ObservableCollection<AccountListItemViewModel> Accounts =>
        _accountsHost?.Accounts ?? new ObservableCollection<AccountListItemViewModel>();

    /// <summary>True iff at least one account is enrolled. Drives the empty-state
    /// caption inside the ACCOUNTS section.</summary>
    public bool HasAccounts => Accounts.Count > 0;

    /// <summary>Remove-account command surfaced from the host.</summary>
    public IAsyncRelayCommand<SignedInAccount?>? RemoveAccountCommand => _accountsHost?.RemoveAccountCommand;

    /// <summary>Add-account command surfaced from the host (opens the slide-in).</summary>
    public IRelayCommand? OpenAddAccountPanelCommand => _accountsHost?.OpenAddAccountPanelCommand;

    /// <summary>Select-account command surfaced from the host.</summary>
    public IRelayCommand<SignedInAccount?>? SelectAccountCommand => _accountsHost?.SelectAccountCommand;

    /// <summary>
    /// Wires the accounts host (typically <see cref="ShellViewModel"/>) after
    /// both VMs exist — see <see cref="IAccountsHost"/> for rationale. Re-raises
    /// <see cref="HasAccounts"/> when the underlying collection mutates so the
    /// empty-state caption flips correctly.
    /// </summary>
    public void AttachAccountsHost(IAccountsHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _accountsHost = host;
        _accountsHost.Accounts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAccounts));
        };

        OnPropertyChanged(nameof(Accounts));
        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(RemoveAccountCommand));
        OnPropertyChanged(nameof(OpenAddAccountPanelCommand));
        OnPropertyChanged(nameof(SelectAccountCommand));
    }

    /// <summary>Seeds the controls from the current settings + autostart state and slides the panel in.</summary>
    public void Open()
    {
        _suppressPersist = true;
        try
        {
            var current = _userSettings.Current;
            SelectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == current.Theme) ?? ThemeOptions[0];
            SelectedDuration = DurationOptions.FirstOrDefault(o => o.Hours == current.DefaultDurationHours)
                ?? DurationOptions[0];
            SelectedExpiryThreshold = ExpiryThresholdOptions.FirstOrDefault(o => o.Minutes == current.ExpiryWarningMinutes)
                ?? ExpiryThresholdOptions[0];
            ExpiryWarningEnabled = current.ExpiryWarningEnabled;
            AutomaticUpdatesEnabled = current.AutomaticUpdatesEnabled;
            IsAccountsSectionExpanded = current.SettingsAccountsExpanded;

            // Pre-fill the ClientId input with the current effective value
            // when it's already a real GUID; otherwise leave it blank so the
            // placeholder hint shows.
            ClientIdInput = IsCurrentClientIdMissing ? string.Empty : _options.ClientId;
            ShowRestartPrompt = false;

            // Pulled live from the registry so a parallel toggle in the tray
            // menu is reflected even mid-session.
            StartWithWindows = _autostart.IsEnabled;

            // Likewise read live from disk so the shortcut's actual presence
            // (it may have been removed in the first-run dialog) drives the toggle.
            CreateStartMenuShortcut = _shortcuts.IsStartMenuShortcutPresent;
        }
        finally
        {
            _suppressPersist = false;
        }

        IsOpen = true;
    }

    /// <summary>
    /// Persists the entered ClientId to the per-user
    /// <c>appsettings.local.json</c> and surfaces the restart-required banner.
    /// The new ClientId only takes effect on the next process launch because
    /// MSAL's PCA is built once per cloud at startup.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsClientIdInputValid))]
    private void SaveClientId()
    {
        try
        {
            LocalConfigStore.SaveClientId(ClientIdInput.Trim());
            ShowRestartPrompt = true;
            _logger.LogInformation("App Registration ClientId saved; awaiting restart.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save App Registration ClientId");
        }
    }

    /// <summary>
    /// Launches a fresh process from the current executable and shuts the
    /// running one down. Used after <see cref="SaveClientId"/> so the new
    /// configuration is picked up via <c>appsettings.local.json</c>.
    /// </summary>
    [RelayCommand]
    private void RestartApp()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start the replacement process for restart");
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    /// <summary>
    /// Opens the folder holding the rolling Serilog files in the OS file manager.
    /// Path must match the sink configured in <c>App.BuildHost</c>:
    /// <c>%LocalAppData%\Entra-PIM-Manager\logs</c>. The directory is created at
    /// startup, so it normally exists; we create it defensively in case logging
    /// failed to initialize.
    /// </summary>
    [RelayCommand]
    private void OpenLog()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            Process.Start(new ProcessStartInfo(LogDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open the log folder at {LogDirectory}", LogDirectory);
        }
    }

    /// <summary>
    /// Asks the updater to check GitHub for a newer release right now and surface
    /// the prompt if one is found. Backs the "Check for updates" button; the
    /// actual work happens in <see cref="Tray.UpdateController"/> via
    /// <see cref="CheckForUpdatesRequested"/>.
    /// </summary>
    [RelayCommand]
    private async Task CheckForUpdatesNow()
    {
        if (CheckForUpdatesRequested is { } handler)
        {
            await handler.Invoke();
        }
    }

    partial void OnClientIdInputChanged(string value)
    {
        SaveClientIdCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsClientIdInputValid));
    }

    partial void OnIsOpenChanged(bool value) => OnPropertyChanged(nameof(PanelOffsetX));

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        if (_suppressPersist)
        {
            return;
        }

        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = ThemeMapper.ToVariant(value.Value);
        }

        SchedulePersist();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_suppressPersist)
        {
            return;
        }

        try
        {
            if (value)
            {
                _autostart.Enable();
            }
            else
            {
                _autostart.Disable();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply autostart toggle from settings panel");
        }

        // Autostart isn't part of the persisted UserSettings record (registry is
        // the source of truth) — no SaveAsync call needed here.
    }

    partial void OnCreateStartMenuShortcutChanged(bool value)
    {
        if (_suppressPersist)
        {
            return;
        }

        if (value)
        {
            _shortcuts.EnableStartMenuShortcut();
        }
        else
        {
            _shortcuts.DisableStartMenuShortcut();
        }

        // Like autostart, the shortcut file itself is the source of truth — it is
        // not mirrored into the persisted UserSettings record, so no SaveAsync here.
    }

    partial void OnSelectedDurationChanged(DurationOption value)
    {
        if (_suppressPersist)
        {
            return;
        }

        SchedulePersist();
    }

    partial void OnExpiryWarningEnabledChanged(bool value)
    {
        if (_suppressPersist)
        {
            return;
        }

        SchedulePersist();
    }

    partial void OnAutomaticUpdatesEnabledChanged(bool value)
    {
        if (_suppressPersist)
        {
            return;
        }

        SchedulePersist();
    }

    partial void OnSelectedExpiryThresholdChanged(ExpiryThresholdOption value)
    {
        if (_suppressPersist)
        {
            return;
        }

        SchedulePersist();
    }

    partial void OnIsAccountsSectionExpandedChanged(bool value)
    {
        if (_suppressPersist)
        {
            return;
        }

        SchedulePersist();
    }

    [RelayCommand]
    private void ToggleAccountsSection() => IsAccountsSectionExpanded = !IsAccountsSectionExpanded;

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        Closed?.Invoke();
    }

    private void SchedulePersist()
    {
        // Fire-and-forget — the partial void handlers run on the UI thread
        // and can't be async themselves. PersistAsync swallows errors and
        // logs so a transient IO failure doesn't crash the dispatcher.
        _ = PersistAsync();
    }

    private async Task PersistAsync()
    {
        // `with` instead of `new`: the record now carries shell-layout fields
        // (LastUsedAccountKey, ExpandedTenants) that the Settings panel does
        // NOT own. A fresh `new UserSettings(...)` would silently wipe them.
        var settings = _userSettings.Current with
        {
            Theme = SelectedTheme.Value,
            DefaultDurationHours = SelectedDuration.Hours,
            ExpiryWarningEnabled = ExpiryWarningEnabled,
            ExpiryWarningMinutes = SelectedExpiryThreshold.Minutes,
            SettingsAccountsExpanded = IsAccountsSectionExpanded,
            AutomaticUpdatesEnabled = AutomaticUpdatesEnabled,
        };

        try
        {
            await _userSettings.SaveAsync(settings).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist user settings from the settings panel");
        }
    }

    /// <summary>ComboBox row: pairs the <see cref="ThemePreference"/> value with the label shown to the user.</summary>
    public sealed record ThemeOption(ThemePreference Value, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>ComboBox row: pairs the default duration in hours with the label shown to the user.</summary>
    public sealed record DurationOption(double Hours, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>ComboBox row: pairs the expiry-warning threshold (minutes) with the label shown to the user.</summary>
    public sealed record ExpiryThresholdOption(int Minutes, string Label)
    {
        public override string ToString() => Label;
    }
}
