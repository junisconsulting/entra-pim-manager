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
using EntraPimManager.Core.Collections;
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
    /// The registrations as they now stand on disk. Seeded from the startup options
    /// snapshot and kept in step with every save and removal, because the file itself is
    /// only read again at launch.
    /// </summary>
    private readonly List<TenantAppRegistration> _registrations = [];

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
    /// The user's own choice for the TENANTS section, which is what
    /// <see cref="UserSettings.SettingsAccountsExpanded"/> stores. Distinct from
    /// <see cref="IsTenantsSectionExpanded"/>, which <see cref="Open"/> may force open.
    /// </summary>
    private bool _persistedTenantsExpanded = true;

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
    /// Bound to the log-detail ComboBox in DIAGNOSTICS. Applied live through
    /// the Serilog level switch (App subscribes to the settings Changed
    /// event), so no restart is needed.
    /// </summary>
    [ObservableProperty]
    private LogLevelOption _selectedLogLevel;

    /// <summary>
    /// Whether the TENANTS section is expanded. Persisted in
    /// <see cref="UserSettings.SettingsAccountsExpanded"/> (the field predates the merge
    /// of the two sections; renaming the JSON key would break existing settings files),
    /// but forced open on <see cref="Open"/> while any tenant still needs attention.
    /// </summary>
    [ObservableProperty]
    private bool _isTenantsSectionExpanded = true;

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
    /// The "add a tenant" form at the bottom of the section. Both ids must parse as GUIDs
    /// before <see cref="AddTenantCommand"/> enables; the label is optional. An existing
    /// tenant is edited in its own card instead — see <see cref="TenantNodeViewModel"/>.
    /// Cleared on every <see cref="Open"/> and after a successful add.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddRegistration))]
    [NotifyCanExecuteChangedFor(nameof(AddTenantCommand))]
    private string _newTenantId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddRegistration))]
    [NotifyCanExecuteChangedFor(nameof(AddTenantCommand))]
    private string _newClientId = string.Empty;

    [ObservableProperty]
    private string _newLabel = string.Empty;

    /// <summary>
    /// The tenant's ticketing system, prefilled into the activation form. Unlike the
    /// rest of this form it is stored in <see cref="UserSettings.TicketSystems"/> and
    /// takes effect at once — it is workflow, not auth configuration.
    /// </summary>
    [ObservableProperty]
    private string _newTicketSystem = string.Empty;

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

        // Configuration order — the same order as the "Sign in with" picker. The
        // validator has already rejected unknown cloud names at startup.
        _registrations.AddRange(_options.TenantAppRegistrations.Select(Clone));
        _newTenantCloud = CloudOptions[0];
        SyncTenantNodes();
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
    /// The tenants, each with its registration and the accounts signed into it. The
    /// union of what is configured and what is enrolled: removing a registration leaves
    /// its accounts in place by design, and they must still have a card to sit under.
    /// </summary>
    public ObservableCollection<TenantNodeViewModel> Nodes { get; } = [];

    /// <summary>Cloud choices for the form, in <see cref="EntraCloud"/> declaration order.</summary>
    public IReadOnlyList<CloudOption> CloudOptions { get; } = [.. Enum.GetValues<EntraCloud>()
        .Select(c => new CloudOption(c, EntraCloudInfo.DisplayName(c)))];

    /// <summary>Save is allowed once both ids of the form parse as GUIDs.</summary>
    public bool CanAddRegistration => Guid.TryParse(NewTenantId, out _) && Guid.TryParse(NewClientId, out _);

    /// <summary>
    /// True when at least one registration exists and every one of them has been proven
    /// by a sign-in. Drives the single "Verified" badge in the section header.
    /// </summary>
    /// <remarks>
    /// Counts only tenants that actually have a registration. A tenant whose registration
    /// was removed but whose accounts remain has nothing to verify, and letting it into
    /// this test would suppress the badge for a setup that is entirely fine.
    /// </remarks>
    public bool AreAppRegistrationsVerified =>
        Nodes.Any(n => n.HasRegistration) && Nodes.Where(n => n.HasRegistration).All(n => n.IsVerified);

    /// <summary>
    /// Folder holding the rolling Serilog files — the same location
    /// <c>App.BuildHost</c> configures the file sink to write to.
    /// </summary>
    public string LogDirectory => AppPaths.LogDirectory;

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
        _accountsHost.Accounts.CollectionChanged += (_, _) => SyncTenantNodes();
        SyncTenantNodes();
    }

    /// <summary>
    /// Re-raises the App Registration verification properties. Called by the
    /// shell when a sign-in proves the configuration while the panel is open,
    /// so the card flips to its verified state without a reopen.
    /// </summary>
    public void NotifyAppRegistrationVerificationChanged()
    {
        foreach (var node in Nodes)
        {
            node.NotifyStateChanged();
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
            SyncTenantNodes();
            ClearForm();
            ShowRestartPrompt = false;

            // A fully proven setup honours whatever the user last chose; as long as any
            // tenant is unconfigured or unproven — or none exists yet — the section opens
            // anyway, so the next step is visible without hunting for it.
            // Kept apart from what gets persisted: forcing the section open must not
            // overwrite the user's own collapse preference the next time anything else
            // in Settings is saved.
            _persistedTenantsExpanded = current.SettingsAccountsExpanded;
            IsTenantsSectionExpanded = _persistedTenantsExpanded
                || Nodes.Count == 0
                || Nodes.Any(n => !n.HasRegistration || !n.IsVerified);

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
    /// A detached copy of a configured registration. The options object is what MSAL
    /// resolves client ids from on every call, so editing an entry in place would apply
    /// the change at once — while the banner promises it takes effect on restart, and the
    /// new client id has no token cache yet.
    /// </summary>
    private static TenantAppRegistration Clone(TenantAppRegistration registration)
        => new()
        {
            TenantId = registration.TenantId,
            ClientId = registration.ClientId,
            Cloud = registration.Cloud,
            Label = registration.Label,
        };

    /// <summary>The slot a configured registration occupies, or <c>null</c> for an unparseable cloud.</summary>
    private static TenantSlot? SlotOf(TenantAppRegistration registration)
        => Enum.TryParse<EntraCloud>(registration.Cloud, ignoreCase: true, out var cloud)
            ? new TenantSlot(cloud, registration.TenantId)
            : null;

    /// <summary>
    /// Brings <see cref="Nodes"/> in line with the configured registrations and the
    /// enrolled accounts. Cheap and idempotent on purpose: it runs on every account
    /// change, and enrolling N accounts at startup fires it N+1 times.
    /// </summary>
    /// <remarks>
    /// Diffed rather than rebuilt. Clearing the collection would make the list drop and
    /// recreate every row, and a recreated row loses keyboard focus — which would throw
    /// the user out of the alias box mid-word whenever a refresh landed. See
    /// <see cref="ObservableCollectionSync"/>.
    /// </remarks>
    private void SyncTenantNodes()
    {
        var accountRows = _accountsHost?.Accounts.ToList() ?? [];
        var accountSlots = accountRows.ConvertAll(a => new TenantSlot(a.Account.Cloud, a.Account.TenantId));
        var registrationSlots = _registrations.Select(SlotOf).OfType<TenantSlot>().ToList();

        var desired = new List<TenantNodeViewModel>();
        foreach (var slot in TenantSlot.Merge(accountSlots, registrationSlots))
        {
            var node = Nodes.FirstOrDefault(n => string.Equals(n.Key, slot.Key, StringComparison.Ordinal))
                ?? new TenantNodeViewModel(slot, VerifiedClientIds, SaveNode, RemoveNode, AddAccountToNode);

            // Sign-in needs both: an entry the user has not removed, and one the startup
            // configuration already knew about. A removal disables the button before the
            // restart that enacts it; a fresh addition until the restart that loads it.
            var registration = _registrations.FirstOrDefault(r => SlotOf(r)?.Key == slot.Key);
            node.UpdateRegistration(
                registration?.ClientId,
                registration?.Label,
                _userSettings.Current.TicketSystemFor(slot.TenantId),
                registration is not null && _options.ClientIdFor(slot.Cloud, slot.TenantId) is not null);

            var rows = accountRows
                .Where(a => new TenantSlot(a.Account.Cloud, a.Account.TenantId).Key == slot.Key)
                .ToList();
            ObservableCollectionSync.Apply(node.Accounts, rows);
            desired.Add(node);
        }

        ObservableCollectionSync.Apply(Nodes, desired);
        OnPropertyChanged(nameof(AreAppRegistrationsVerified));
    }

    /// <summary>
    /// Saves one tenant card: its client id and label to the per-user
    /// <c>appsettings.local.json</c>, its ticketing system to the user settings.
    /// </summary>
    /// <remarks>
    /// The two halves are deliberately not saved alike. A registration change only takes
    /// effect on the next launch, because MSAL's clients are built from the startup
    /// configuration — so it writes the file and raises the restart banner. The ticketing
    /// system is workflow, applies at once, and must not make the app ask for a restart
    /// it does not need.
    /// </remarks>
    private void SaveNode(TenantNodeViewModel node)
    {
        var clientId = node.ClientIdDraft.Trim();
        var label = string.IsNullOrWhiteSpace(node.LabelDraft) ? null : node.LabelDraft.Trim();
        var existing = _registrations.FirstOrDefault(r => SlotOf(r)?.Key == node.Key);
        var registrationChanged = existing is null
            || !string.Equals(existing.ClientId, clientId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.Label, label, StringComparison.Ordinal);

        if (registrationChanged)
        {
            try
            {
                LocalConfigStore.SaveTenantRegistration(
                    AppPaths.LocalConfigFile,
                    new TenantAppRegistration
                    {
                        TenantId = node.TenantId,
                        ClientId = clientId,
                        Cloud = node.Cloud.ToString(),
                        Label = label,
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save the App Registration for cloud {Cloud}, tenant {TenantId}", node.Cloud, node.TenantId);
                return;
            }

            if (existing is null)
            {
                _registrations.Add(new TenantAppRegistration
                {
                    TenantId = node.TenantId,
                    ClientId = clientId,
                    Cloud = node.Cloud.ToString(),
                    Label = label,
                });
            }
            else
            {
                // Mutated rather than replaced, so the entry keeps its position in the
                // configuration order the tenant list falls back to.
                existing.ClientId = clientId;
                existing.Label = label;
            }

            ShowRestartPrompt = true;
            _logger.LogInformation(
                "App Registration saved for cloud {Cloud}, tenant {TenantId}; awaiting restart.",
                node.Cloud,
                node.TenantId);
        }

        SaveTicketSystem(
            node.TenantId,
            string.IsNullOrWhiteSpace(node.TicketSystemDraft) ? null : node.TicketSystemDraft.Trim());

        node.IsConfigExpanded = false;
        SyncTenantNodes();
    }

    /// <summary>
    /// Removes a tenant's registration from the per-user config. Accounts already
    /// enrolled through it keep their entries and their card; after the restart they have
    /// no registration to authenticate with, and the card says so.
    /// </summary>
    private void RemoveNode(TenantNodeViewModel node)
    {
        try
        {
            LocalConfigStore.RemoveTenantRegistration(AppPaths.LocalConfigFile, node.Cloud, node.TenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove the App Registration for cloud {Cloud}, tenant {TenantId}", node.Cloud, node.TenantId);
            return;
        }

        var existing = _registrations.FirstOrDefault(r => SlotOf(r)?.Key == node.Key);
        if (existing is not null)
        {
            _registrations.Remove(existing);
        }

        ShowRestartPrompt = true;
        _logger.LogInformation(
            "App Registration removed for cloud {Cloud}, tenant {TenantId}; awaiting restart.",
            node.Cloud,
            node.TenantId);
        SyncTenantNodes();
    }

    /// <summary>Opens the sign-in slide-in with this tenant already chosen.</summary>
    private void AddAccountToNode(TenantNodeViewModel node)
        => _accountsHost?.OpenAddAccountPanelCommand.Execute(node.Slot);

    /// <summary>
    /// Adds a tenant nobody has configured yet. Editing an existing one happens in its
    /// own card — its tenant id and cloud are its identity, so changing either is a
    /// removal and an addition, not an edit.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddRegistration))]
    private void AddTenant()
    {
        var cloud = NewTenantCloud.Cloud;
        var tenantId = Guid.Parse(NewTenantId.Trim()).ToString();
        var clientId = NewClientId.Trim();
        var label = string.IsNullOrWhiteSpace(NewLabel) ? null : NewLabel.Trim();

        try
        {
            LocalConfigStore.SaveTenantRegistration(
                AppPaths.LocalConfigFile,
                new TenantAppRegistration { TenantId = tenantId, ClientId = clientId, Cloud = cloud.ToString(), Label = label });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save the App Registration for cloud {Cloud}, tenant {TenantId}", cloud, tenantId);
            return;
        }

        var slotKey = new TenantSlot(cloud, tenantId).Key;
        var existing = _registrations.FirstOrDefault(r => SlotOf(r)?.Key == slotKey);
        if (existing is null)
        {
            _registrations.Add(new TenantAppRegistration { TenantId = tenantId, ClientId = clientId, Cloud = cloud.ToString(), Label = label });
        }
        else
        {
            existing.ClientId = clientId;
            existing.Label = label;
        }

        // Not part of the registration file — see NewTicketSystem. Saved after the
        // registration write so a failed write never leaves a half-applied form.
        SaveTicketSystem(tenantId, string.IsNullOrWhiteSpace(NewTicketSystem) ? null : NewTicketSystem.Trim());

        ClearForm();
        ShowRestartPrompt = true;
        _logger.LogInformation(
            "App Registration saved for cloud {Cloud}, tenant {TenantId}; awaiting restart.",
            cloud,
            tenantId);
        SyncTenantNodes();
    }

    /// <summary>
    /// Launches a fresh process from the current executable and shuts the
    /// running one down. Used after <see cref="SaveRegistration"/> so the new
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

    partial void OnIsTenantsSectionExpandedChanged(bool value)
    {
        if (_suppressPersist)
        {
            return;
        }

        SchedulePersist();
    }

    [RelayCommand]
    private void ToggleTenantsSection()
    {
        // The field first: setting the property runs the change handler, which persists
        // synchronously up to its first await — so assigning afterwards would write the
        // value from before this toggle and leave the preference one click behind.
        _persistedTenantsExpanded = !IsTenantsSectionExpanded;
        IsTenantsSectionExpanded = _persistedTenantsExpanded;
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        Closed?.Invoke();
    }

    private string[] VerifiedClientIds() => _userSettings.Current.VerifiedClientIds ?? [];

    private void ClearForm()
    {
        NewTenantId = string.Empty;
        NewClientId = string.Empty;
        NewLabel = string.Empty;
        NewTicketSystem = string.Empty;
        NewTenantCloud = CloudOptions[0];
    }

    /// <summary>
    /// Stores (or clears, when blank) the tenant's ticketing system. Keyed by tenant
    /// id alone — a tenant lives in exactly one cloud, so the cloud adds nothing.
    /// </summary>
    private void SaveTicketSystem(string tenantId, string? ticketSystem)
    {
        var updated = _userSettings.Current;
        var systems = updated.TicketSystems is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(updated.TicketSystems, StringComparer.OrdinalIgnoreCase);
        if (ticketSystem is null)
        {
            systems.Remove(tenantId);
        }
        else
        {
            systems[tenantId] = ticketSystem;
        }

        _ = SaveSettingsAsync(updated with { TicketSystems = systems });
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
            SettingsAccountsExpanded = _persistedTenantsExpanded,
            AutomaticUpdatesEnabled = AutomaticUpdatesEnabled,
            LogLevel = SelectedLogLevel.Value,
        };

        await SaveSettingsAsync(settings).ConfigureAwait(false);
    }

    private async Task SaveSettingsAsync(UserSettings settings)
    {
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
