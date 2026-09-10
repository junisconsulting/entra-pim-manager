namespace EntraPimManager.AppAvalonia.Tray;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using EntraPimManager.AppAvalonia.Services;
using EntraPimManager.AppAvalonia.ViewModels;
using EntraPimManager.AppAvalonia.Views;
using EntraPimManager.Core.Configuration;
using EntraPimManager.Core.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Owns the single re-usable update-prompt window and the periodic update check.
/// Polls GitHub Releases (an initial check shortly after launch, then once a
/// day), and when a newer version is found shows an always-on-top prompt
/// anchored bottom-right above the tray. On accept it downloads in the
/// background and offers "Restart now" or applies on the next launch. The
/// polling half is gated by <see cref="UserSettings.AutomaticUpdatesEnabled"/>
/// (live-read each tick); <see cref="CheckNowAsync"/> — the Settings button — is
/// not. Either way, nothing runs outside a real Velopack install.
/// </summary>
public sealed class UpdateController
{
    private const int ScreenMargin = 12;

    // Logical-height estimate used to anchor the window before its first layout
    // pass populates the real Bounds; corrected immediately afterwards.
    private const double EstimatedHeight = 170;

    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(24);

    private readonly UpdatePromptWindow _window;
    private readonly UpdatePromptViewModel _viewModel;
    private readonly IUpdateService _updateService;
    private readonly IUserSettingsService _settings;
    private readonly ILogger<UpdateController> _logger;
    private readonly DispatcherTimer _timer;

    // Versions the user dismissed this session — don't nag again until restart.
    private readonly HashSet<string> _dismissedVersions = new(StringComparer.Ordinal);

    private UpdateCheckResult? _pending;
    private bool _allowClose;
    private bool _busy;

    public UpdateController(
        UpdatePromptWindow window,
        UpdatePromptViewModel viewModel,
        IUpdateService updateService,
        IUserSettingsService settings,
        SettingsPanelViewModel settingsPanel,
        ILogger<UpdateController> logger)
    {
        ArgumentNullException.ThrowIfNull(settingsPanel);
        _window = window;
        _viewModel = viewModel;
        _updateService = updateService;
        _settings = settings;
        _logger = logger;
        _window.DataContext = _viewModel;

        // Hide instead of closing on the OS close action — the window is reused.
        _window.Closing += (_, args) =>
        {
            if (_allowClose)
            {
                return;
            }

            args.Cancel = true;
            _window.Hide();
        };

        _window.InstallRequested += OnInstallRequested;
        _window.RestartRequested += OnRestartRequested;
        _window.LaterRequested += OnLaterRequested;

        // The Settings "Check for updates" button routes here (the VM has no
        // direct controller dependency — it just raises this event and renders
        // whatever outcome comes back).
        settingsPanel.CheckForUpdatesRequested += CheckNowAsync;

        _timer = new DispatcherTimer { Interval = InitialDelay };
        _timer.Tick += OnTimerTick;
    }

    /// <summary>Allows the next window-close to actually close (called on app exit).</summary>
    public void PrepareForShutdown() => _allowClose = true;

    /// <summary>
    /// Starts the periodic check. No-ops (no timer, no window) when the app is
    /// not a Velopack install, so dev / cross-build runs are unaffected.
    /// </summary>
    public void Start()
    {
        if (!_updateService.IsSupported)
        {
            _logger.LogDebug("Auto-update disabled — not running from a Velopack install");
            return;
        }

        _timer.Start();
    }

    /// <summary>
    /// Runs a check immediately and reports what it found. Backs the Settings
    /// "Check for updates" button.
    /// </summary>
    /// <remarks>
    /// Deliberately ignores both brakes the background poll obeys. The
    /// <see cref="UserSettings.AutomaticUpdatesEnabled"/> switch governs
    /// unprompted checking, not the user's ability to ask; and a version
    /// dismissed earlier this session was dismissed against a popup nobody
    /// asked for, so a deliberate press must still be able to see it.
    /// </remarks>
    public Task<UpdateCheckOutcome> CheckNowAsync() => CheckAsync(manual: true);

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        // The first tick fires after InitialDelay; from then on poll daily.
        _timer.Interval = PollInterval;

        // Live-read so toggling the setting takes effect without a restart.
        if (_busy || !_settings.Current.AutomaticUpdatesEnabled)
        {
            return;
        }

        await CheckAsync(manual: false);
    }

    private async Task<UpdateCheckOutcome> CheckAsync(bool manual)
    {
        // A download in flight, or one already finished and waiting for the restart,
        // owns the prompt. Running the check through would reset it to Available with
        // Progress 0 — discarding a finished download and offering the same version a
        // second time. The honest answer to "is there an update" is the prompt that
        // already exists: a deliberate press brings its window back, the daily poll
        // leaves a window the user hid alone.
        if (_pending is not null
            && _viewModel.Stage is UpdatePromptViewModel.UpdateStage.Downloading
                or UpdatePromptViewModel.UpdateStage.Ready)
        {
            if (manual)
            {
                Show();
            }

            return UpdateCheckOutcome.UpdateAvailable;
        }

        _busy = true;
        try
        {
            var (outcome, update) = await _updateService.CheckAsync().ConfigureAwait(true);
            if (update is null || (!manual && _dismissedVersions.Contains(update.Version)))
            {
                return outcome;
            }

            _pending = update;
            _viewModel.Stage = UpdatePromptViewModel.UpdateStage.Available;
            _viewModel.NewVersion = update.Version;
            _viewModel.CurrentVersion = _updateService.CurrentVersion ?? string.Empty;
            _viewModel.Progress = 0;
            Show();
            return outcome;
        }
        finally
        {
            _busy = false;
        }
    }

    private async void OnInstallRequested(object? sender, EventArgs e)
    {
        if (_pending is not { } update)
        {
            return;
        }

        _viewModel.Progress = 0;
        _viewModel.Stage = UpdatePromptViewModel.UpdateStage.Downloading;
        try
        {
            await _updateService
                .DownloadAsync(update, p => Dispatcher.UIThread.Post(() => _viewModel.Progress = p))
                .ConfigureAwait(true);
            _viewModel.Stage = UpdatePromptViewModel.UpdateStage.Ready;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update download failed");
            _viewModel.ErrorText = "The update could not be downloaded. Please try again later.";
            _viewModel.Stage = UpdatePromptViewModel.UpdateStage.Error;
        }
    }

    private void OnRestartRequested(object? sender, EventArgs e)
    {
        if (_pending is { } update)
        {
            // Exits and relaunches into the new version.
            _updateService.ApplyAndRestart(update);
        }
    }

    private void OnLaterRequested(object? sender, EventArgs e)
    {
        if (_pending is { } update)
        {
            _dismissedVersions.Add(update.Version);

            // Already downloaded? Stage it so the next launch is the new version
            // without a second download.
            if (_viewModel.Stage == UpdatePromptViewModel.UpdateStage.Ready)
            {
                _updateService.ApplyOnNextLaunch(update);
            }
        }

        Hide();
    }

    private void Show()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_window.IsVisible)
            {
                return;
            }

            AnchorBottomRight(EstimatedHeight);
            _window.Show();

            // Re-anchor once the real height is known (SizeToContent only fills
            // Bounds after the first layout pass).
            Dispatcher.UIThread.Post(() => AnchorBottomRight(null), DispatcherPriority.Loaded);
        });
    }

    private void Hide() => Dispatcher.UIThread.Post(() =>
    {
        if (_window.IsVisible)
        {
            _window.Hide();
        }
    });

    private void AnchorBottomRight(double? heightOverride)
    {
        var screen = _window.Screens.Primary ?? _window.Screens.All.FirstOrDefault();
        var bounds = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);

        var scale = _window.DesktopScaling;
        var logicalHeight = heightOverride
            ?? (_window.Bounds.Height > 0 ? _window.Bounds.Height : EstimatedHeight);

        var width = (int)(_window.Width * scale);
        var height = (int)(logicalHeight * scale);

        var x = bounds.Right - width - ScreenMargin;
        var y = bounds.Bottom - height - ScreenMargin;
        _window.Position = new PixelPoint(x, y);
    }
}
