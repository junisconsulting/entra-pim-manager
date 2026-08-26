namespace EntraPimManager.AppAvalonia.ViewModels;

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EntraPimManager.AppAvalonia.Services;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;
using EntraPimManager.Core.Diagnostics;
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
    /// <summary>
    /// Outer guard for the whole network-check run; each probe already caps
    /// itself at 10 s and all probes run in parallel, so this never fires in
    /// practice.
    /// </summary>
    private static readonly TimeSpan NetworkCheckTimeout = TimeSpan.FromSeconds(30);

    private readonly IUserSettingsService _userSettings;
    private readonly IAutostartService _autostart;
    private readonly IShortcutService _shortcuts;
    private readonly EntraPimManagerOptions _options;
    private readonly INetworkDiagnosticsService _networkDiagnostics;
    private readonly ILogger<SettingsPanelViewModel> _logger;

    /// <summary>
    /// The plain-text report of the last completed network check — what the
    /// "Copy report" button puts on the clipboard.
    /// </summary>
    private string? _lastNetworkReportText;

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

    /// <summary>
    /// The registration row currently loaded into the form, or <c>null</c> while
    /// the form adds a new one. Saving replaces this row — and drops its old
    /// configuration slot first if the edit moved it to another cloud or tenant.
    /// </summary>
    private AppRegistrationRowViewModel? _editing;

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
    /// Bound to the log-detail ComboBox in DIAGNOSTICS. Applied live through
    /// the Serilog level switch (App subscribes to the settings Changed
    /// event), so no restart is needed.
    /// </summary>
    [ObservableProperty]
    private LogLevelOption _selectedLogLevel;

    /// <summary>
    /// Whether the ACCOUNTS section is expanded. Persisted in
    /// <see cref="UserSettings.SettingsAccountsExpanded"/>; defaults to
    /// expanded so first-run users see the section is there.
    /// </summary>
    [ObservableProperty]
    private bool _isAccountsSectionExpanded = true;

    /// <summary>
    /// Whether the APP REGISTRATION section is expanded. Not persisted — it is
    /// derived on each <see cref="Open"/> from whether the configuration is
    /// verified, so a proven setup stays out of the way while an unproven one
    /// keeps asking for attention.
    /// </summary>
    [ObservableProperty]
    private bool _isAppRegistrationSectionExpanded = true;

    /// <summary>
    /// True after the user successfully saved a client id. Drives the
    /// inline "Restart required" banner — the new value only takes effect
    /// on the next process start.
    /// </summary>
    [ObservableProperty]
    private bool _showRestartPrompt;

    [ObservableProperty]
    private bool _isNetworkCheckRunning;

    [ObservableProperty]
    private bool _hasNetworkCheckResults;

    /// <summary>True right after a copy, until the next run — drives the "Copied" hint.</summary>
    [ObservableProperty]
    private bool _reportCopied;

    [ObservableProperty]
    private IReadOnlyList<NetworkCheckGroupViewModel> _networkCheckGroups = [];

    /// <summary>
    /// The registration form — adds a new entry, or edits the row loaded via
    /// <see cref="EditRegistrationCommand"/>. The client id must parse as a GUID
    /// before <see cref="SaveRegistrationCommand"/> enables; the tenant id is
    /// either blank (cloud-wide, multi-tenant) or a GUID (pinned, single-tenant);
    /// the label only applies to pinned entries. Cleared on every <see cref="Open"/>,
    /// on cancel and after a successful save.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddRegistration))]
    [NotifyPropertyChangedFor(nameof(IsNewTenantPinned))]
    [NotifyCanExecuteChangedFor(nameof(SaveRegistrationCommand))]
    private string _newTenantId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddRegistration))]
    [NotifyCanExecuteChangedFor(nameof(SaveRegistrationCommand))]
    private string _newClientId = string.Empty;

    /// <summary>
    /// Title of the row being edited, shown above the form; <c>null</c> while adding.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(SubmitLabel))]
    private string? _editingTitle;

    [ObservableProperty]
    private string _newLabel = string.Empty;

    [ObservableProperty]
    private CloudOption _newTenantCloud;

    public SettingsPanelViewModel(
        IUserSettingsService userSettings,
        IAutostartService autostart,
        IShortcutService shortcuts,
        IOptions<EntraPimManagerOptions> options,
        INetworkDiagnosticsService networkDiagnostics,
        ILogger<SettingsPanelViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _userSettings = userSettings;
        _autostart = autostart;
        _shortcuts = shortcuts;
        _options = options.Value;
        _networkDiagnostics = networkDiagnostics;
        _logger = logger;

        _selectedTheme = ThemeOptions[0];
        _selectedDuration = DurationOptions[0];
        _selectedExpiryThreshold = ExpiryThresholdOptions[0];
        _selectedLogLevel = LogLevelOptions[1];

        // Cloud-wide registrations first (Global, China), then the pinned ones in
        // configuration order — the same order as the "Sign in with" picker.
        var rows = new List<AppRegistrationRowViewModel>();
        foreach (var cloud in Enum.GetValues<EntraCloud>())
        {
            if (_options.ClientIdFor(cloud) is { } clientId)
            {
                rows.Add(new AppRegistrationRowViewModel(cloud, null, clientId, null, VerifiedClientIds));
            }
        }

        foreach (var pinned in _options.TenantAppRegistrations)
        {
            if (Enum.TryParse<EntraCloud>(pinned.Cloud, ignoreCase: true, out var cloud))
            {
                rows.Add(new AppRegistrationRowViewModel(cloud, pinned.TenantId, pinned.ClientId, pinned.Label, VerifiedClientIds));
            }
        }

        Registrations = [.. rows];
        _newTenantCloud = CloudOptions[0];
    }

    /// <summary>Raised when the panel closes — payload-less; the shell uses it to drop the exclusive-toggle.</summary>
    public event Action? Closed;

    /// <summary>
    /// Raised by "Copy report" with the report text. <see cref="Tray.TrayPopupController"/>
    /// subscribes and forwards to the window's clipboard; the VM has no view dependency.
    /// </summary>
    public event Func<string, Task>? CopyReportRequested;

    /// <summary>
    /// The WAM out-of-process caveat, shown under the results and appended to
    /// the report — single-sourced so UI and ticket text never drift apart.
    /// </summary>
    public string NetworkCheckCaveat => NetworkDiagnosticsReportFormatter.WamCaveat;

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

    /// <summary>Log-detail choices presented in the ComboBox. Index 1 (Normal) is the default.</summary>
    public IReadOnlyList<LogLevelOption> LogLevelOptions { get; } = new[]
    {
        new LogLevelOption(LogLevelPreference.Debug, "Debug (verbose)"),
        new LogLevelOption(LogLevelPreference.Information, "Normal"),
        new LogLevelOption(LogLevelPreference.Warning, "Warnings only"),
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
    /// Every configured App Registration — the cloud-wide one per cloud and the
    /// tenant-pinned ones — mirrored live as the user adds and removes entries. The
    /// file is written immediately; the running MSAL layer only picks changes up
    /// after the restart the banner asks for. See <see cref="AppRegistrationRowViewModel"/>.
    /// </summary>
    public ObservableCollection<AppRegistrationRowViewModel> Registrations { get; }

    /// <summary>Cloud choices for the add form, in <see cref="EntraCloud"/> declaration order.</summary>
    public IReadOnlyList<CloudOption> CloudOptions { get; } = [.. Enum.GetValues<EntraCloud>()
        .Select(c => new CloudOption(c, EntraCloudInfo.DisplayName(c)))];

    /// <summary>Add is allowed once the client id is a GUID and the tenant id is blank or a GUID.</summary>
    public bool CanAddRegistration =>
        Guid.TryParse(NewClientId, out _) && (string.IsNullOrWhiteSpace(NewTenantId) || Guid.TryParse(NewTenantId, out _));

    /// <summary>
    /// True while the form describes a pinned registration. Only those carry a
    /// label — the cloud-wide entry is named after its cloud — so the label box
    /// is disabled otherwise rather than silently dropping the input.
    /// </summary>
    public bool IsNewTenantPinned => !string.IsNullOrWhiteSpace(NewTenantId);

    /// <summary>True while the form edits an existing row rather than adding one.</summary>
    public bool IsEditing => EditingTitle is not null;

    /// <summary>Caption of the form's submit button.</summary>
    public string SubmitLabel => IsEditing ? "Save" : "Add";

    /// <summary>
    /// True when at least one registration exists and every one of them has been
    /// proven by a sign-in. Drives the single "Verified" badge in the section header.
    /// </summary>
    public bool AreAppRegistrationsVerified => Registrations.Count > 0 && Registrations.All(r => r.IsVerified);

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

    /// <summary>
    /// Re-raises the App Registration verification properties. Called by the
    /// shell when a sign-in proves the configuration while the panel is open,
    /// so the card flips to its verified state without a reopen.
    /// </summary>
    public void NotifyAppRegistrationVerificationChanged()
    {
        foreach (var row in Registrations)
        {
            row.NotifyStateChanged();
        }

        OnPropertyChanged(nameof(AreAppRegistrationsVerified));
    }

    /// <summary>Seeds the controls from the current settings + autostart state and slides the panel in.</summary>
    public void Open()
    {
        _suppressPersist = true;
        try
        {
            var current = _userSettings.Current;
            SelectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == current.Theme) ?? ThemeOptions[0];
            SelectedLogLevel = LogLevelOptions.FirstOrDefault(o => o.Value == current.LogLevel) ?? LogLevelOptions[1];
            SelectedDuration = DurationOptions.FirstOrDefault(o => o.Hours == current.DefaultDurationHours)
                ?? DurationOptions[0];
            SelectedExpiryThreshold = ExpiryThresholdOptions.FirstOrDefault(o => o.Minutes == current.ExpiryWarningMinutes)
                ?? ExpiryThresholdOptions[0];
            ExpiryWarningEnabled = current.ExpiryWarningEnabled;
            AutomaticUpdatesEnabled = current.AutomaticUpdatesEnabled;
            IsAccountsSectionExpanded = current.SettingsAccountsExpanded;

            ClearForm();
            ShowRestartPrompt = false;

            // A fully proven setup collapses out of the way; as long as any
            // registration is configured-but-unproven — or none exists yet — the
            // section stays open so the next step is visible without hunting for it.
            IsAppRegistrationSectionExpanded = Registrations.Count == 0 || Registrations.Any(r => !r.IsVerified);

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

    // Two rows occupy the same configuration slot when they share the cloud and
    // are both cloud-wide, or both pin the same tenant GUID.
    private static bool IsSameSlot(AppRegistrationRowViewModel row, EntraCloud cloud, string? tenantId)
        => row.Cloud == cloud
            && (row.TenantId is null
                ? tenantId is null
                : Guid.TryParse(row.TenantId, out var a) && Guid.TryParse(tenantId, out var b) && a == b);

    // A cloud-wide entry is cleared by blanking its cloud key (so the shipped
    // placeholder cannot shine through the merged configuration); a pinned one
    // is dropped from the list.
    private static void RemoveFromConfig(AppRegistrationRowViewModel row)
    {
        if (row.TenantId is null)
        {
            LocalConfigStore.SaveClientId(AppPaths.LocalConfigFile, row.Cloud, string.Empty);
        }
        else
        {
            LocalConfigStore.RemoveTenantRegistration(AppPaths.LocalConfigFile, row.Cloud, row.TenantId);
        }
    }

    /// <summary>
    /// Loads a row into the form so it can be changed in place — a new client id
    /// after the customer re-registered, a nicer label, a moved tenant. Saving then
    /// replaces the row instead of adding a second one.
    /// </summary>
    [RelayCommand]
    private void EditRegistration(AppRegistrationRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        _editing = row;
        NewTenantId = row.TenantId ?? string.Empty;
        NewClientId = row.ClientId;
        NewLabel = row.Label ?? string.Empty;
        NewTenantCloud = CloudOptions.First(o => o.Cloud == row.Cloud);
        EditingTitle = row.Title;
    }

    [RelayCommand]
    private void CancelEdit() => ClearForm();

    /// <summary>
    /// Writes the form to the per-user <c>appsettings.local.json</c> — a blank
    /// tenant id becomes the cloud's cloud-wide registration (<c>AppRegistrations:{Cloud}</c>),
    /// a GUID a tenant-pinned one (<c>TenantAppRegistrations</c>) — replacing an
    /// existing entry for the same cloud + tenant, mirrors it in <see cref="Registrations"/>
    /// and surfaces the restart banner. The change only takes effect on the next
    /// process launch because MSAL's PCAs are built from the startup configuration.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddRegistration))]
    private void SaveRegistration()
    {
        var cloud = NewTenantCloud.Cloud;
        var clientId = NewClientId.Trim();
        var tenantId = IsNewTenantPinned ? Guid.Parse(NewTenantId.Trim()).ToString() : null;
        var label = tenantId is not null && !string.IsNullOrWhiteSpace(NewLabel) ? NewLabel.Trim() : null;

        try
        {
            // An edit that moved the entry to another cloud or tenant would leave
            // its old slot behind — drop that first so the file never holds both.
            if (_editing is { } moved && !IsSameSlot(moved, cloud, tenantId))
            {
                RemoveFromConfig(moved);
                Registrations.Remove(moved);
            }

            if (tenantId is null)
            {
                LocalConfigStore.SaveClientId(AppPaths.LocalConfigFile, cloud, clientId);
            }
            else
            {
                LocalConfigStore.SaveTenantRegistration(
                    AppPaths.LocalConfigFile,
                    new TenantAppRegistration { TenantId = tenantId, ClientId = clientId, Cloud = cloud.ToString(), Label = label });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save the App Registration for cloud {Cloud}, tenant {TenantId}", cloud, tenantId);
            return;
        }

        var row = new AppRegistrationRowViewModel(cloud, tenantId, clientId, label, VerifiedClientIds);
        var existing = Registrations.FirstOrDefault(r => IsSameSlot(r, cloud, tenantId));
        if (existing is not null)
        {
            Registrations[Registrations.IndexOf(existing)] = row;
        }
        else
        {
            Registrations.Add(row);
        }

        ClearForm();
        ShowRestartPrompt = true;
        OnPropertyChanged(nameof(AreAppRegistrationsVerified));
        _logger.LogInformation(
            "App Registration saved for cloud {Cloud}, tenant {TenantId}; awaiting restart.",
            cloud,
            tenantId ?? "<any>");
    }

    /// <summary>
    /// Removes a registration from the per-user config and the list — a cloud-wide
    /// one by blanking its cloud entry (so the shipped placeholder cannot shine
    /// through), a pinned one by dropping its list entry. Accounts already enrolled
    /// through it keep their entries; after the restart they resolve to whatever
    /// registration remains (or none) and their group asks the user to remove and
    /// re-add them.
    /// </summary>
    [RelayCommand]
    private void RemoveRegistration(AppRegistrationRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        try
        {
            RemoveFromConfig(row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove the App Registration for cloud {Cloud}, tenant {TenantId}", row.Cloud, row.TenantId);
            return;
        }

        Registrations.Remove(row);
        if (row == _editing)
        {
            ClearForm();
        }

        ShowRestartPrompt = true;
        OnPropertyChanged(nameof(AreAppRegistrationsVerified));
        _logger.LogInformation(
            "App Registration removed for cloud {Cloud}, tenant {TenantId}; awaiting restart.",
            row.Cloud,
            row.TenantId ?? "<any>");
    }

    /// <summary>
    /// Launches a fresh process from the current executable and shuts the
    /// running one down. Used after <see cref="SaveClientId"/> so the new
    /// configuration is picked up via <c>appsettings.local.json</c>. The
    /// <see cref="Program.RestartArgument"/> makes the replacement wait for
    /// this process to release the single-instance mutex instead of treating
    /// it as an already-running instance and exiting.
    /// </summary>
    [RelayCommand]
    private void RestartApp()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                Process.Start(new ProcessStartInfo(exePath, Program.RestartArgument) { UseShellExecute = true });
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
    /// Probes every endpoint the app and the Windows sign-in window need and
    /// renders the per-endpoint results. Built for the field case where the
    /// WAM window stays blank in a locked-down network: the report proves
    /// whether the environment blocks a required endpoint.
    /// </summary>
    [RelayCommand]
    private async Task RunNetworkCheck()
    {
        IsNetworkCheckRunning = true;
        ReportCopied = false;
        try
        {
            using var cts = new CancellationTokenSource(NetworkCheckTimeout);
            var report = await _networkDiagnostics.RunAsync(cts.Token);
            NetworkCheckGroups = [.. report.Groups.Select(g => new NetworkCheckGroupViewModel(g))];
            HasNetworkCheckResults = true;

            // Entry-assembly version, same source as the footer label.
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            var appVersion = version is null ? "?" : $"{version.Major}.{version.Minor}.{version.Build}";
            _lastNetworkReportText = NetworkDiagnosticsReportFormatter.Format(
                report,
                appVersion,
                Environment.OSVersion.VersionString);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Network check failed");
        }
        finally
        {
            IsNetworkCheckRunning = false;
        }
    }

    /// <summary>Puts the last report on the clipboard via <see cref="CopyReportRequested"/>.</summary>
    [RelayCommand]
    private async Task CopyNetworkReport()
    {
        if (_lastNetworkReportText is null || CopyReportRequested is not { } handler)
        {
            return;
        }

        await handler.Invoke(_lastNetworkReportText);
        ReportCopied = true;
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

    partial void OnSelectedLogLevelChanged(LogLevelOption value)
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
    private void ToggleAppRegistrationSection() =>
        IsAppRegistrationSectionExpanded = !IsAppRegistrationSectionExpanded;

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        Closed?.Invoke();
    }

    private string[] VerifiedClientIds() => _userSettings.Current.VerifiedClientIds ?? [];

    private void ClearForm()
    {
        _editing = null;
        EditingTitle = null;
        NewTenantId = string.Empty;
        NewClientId = string.Empty;
        NewLabel = string.Empty;
        NewTenantCloud = CloudOptions[0];
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
            LogLevel = SelectedLogLevel.Value,
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

    /// <summary>ComboBox row: pairs the <see cref="EntraCloud"/> value with the label shown to the user.</summary>
    public sealed record CloudOption(EntraCloud Cloud, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>ComboBox row: pairs the <see cref="ThemePreference"/> value with the label shown to the user.</summary>
    public sealed record ThemeOption(ThemePreference Value, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>ComboBox row: pairs the <see cref="LogLevelPreference"/> value with the label shown to the user.</summary>
    public sealed record LogLevelOption(LogLevelPreference Value, string Label)
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
