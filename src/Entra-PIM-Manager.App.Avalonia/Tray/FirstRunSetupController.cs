namespace EntraPimManager.AppAvalonia.Tray;

using System;
using System.IO;
using Avalonia.Threading;
using EntraPimManager.AppAvalonia.Services;
using EntraPimManager.AppAvalonia.ViewModels;
using EntraPimManager.AppAvalonia.Views;
using EntraPimManager.Core.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Shows the one-time first-run setup window. Velopack's <c>OnFirstRun</c> hook
/// drops a marker (see <see cref="AppPaths.FirstRunSetupMarkerFile"/>); on the next
/// UI start <see cref="Start"/> picks it up, seeds the toggles from the actual
/// install state, and lets the user opt out of autostart / the Start menu entry.
/// The choice is applied and the marker deleted when the user confirms or closes
/// the window, so it never reappears. Outside a Velopack install no marker is ever
/// written, so this stays dormant in dev / cross-build runs.
/// </summary>
public sealed class FirstRunSetupController
{
    private readonly FirstRunSetupWindow _window;
    private readonly FirstRunSetupViewModel _viewModel;
    private readonly IAutostartService _autostart;
    private readonly IShortcutService _shortcuts;
    private readonly ILogger<FirstRunSetupController> _logger;

    private bool _applied;

    public FirstRunSetupController(
        FirstRunSetupWindow window,
        FirstRunSetupViewModel viewModel,
        IAutostartService autostart,
        IShortcutService shortcuts,
        ILogger<FirstRunSetupController> logger)
    {
        _window = window;
        _viewModel = viewModel;
        _autostart = autostart;
        _shortcuts = shortcuts;
        _logger = logger;
        _window.DataContext = _viewModel;

        _window.ContinueRequested += (_, _) =>
        {
            Apply();
            _window.Close();
        };

        // Closing the window via the title-bar X counts as accepting whatever the
        // toggles currently show — apply before it goes away.
        _window.Closing += (_, _) => Apply();
    }

    /// <summary>
    /// Shows the setup window when the first-run marker is present. No-ops otherwise.
    /// </summary>
    public void Start()
    {
        if (!File.Exists(AppPaths.FirstRunSetupMarkerFile))
        {
            return;
        }

        // Seed from the live state: autostart was defaulted on by OnFirstRun and the
        // installer already created the Start menu shortcut, so both toggles start
        // on unless something failed.
        _viewModel.StartWithWindows = _autostart.IsEnabled;
        _viewModel.CanManageStartMenuShortcut = _shortcuts.IsSupported;
        _viewModel.CreateStartMenuShortcut = !_shortcuts.IsSupported || _shortcuts.IsStartMenuShortcutPresent;

        Dispatcher.UIThread.Post(() => _window.Show());
    }

    private void Apply()
    {
        // Guard: ContinueRequested triggers Close(), which fires Closing — only the
        // first call should do the work.
        if (_applied)
        {
            return;
        }

        _applied = true;

        try
        {
            if (_viewModel.StartWithWindows)
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
            _logger.LogWarning(ex, "First-run setup: applying the autostart choice failed");
        }

        if (_shortcuts.IsSupported)
        {
            if (_viewModel.CreateStartMenuShortcut)
            {
                _shortcuts.EnableStartMenuShortcut();
            }
            else
            {
                _shortcuts.DisableStartMenuShortcut();
            }
        }

        TryClearMarker();
    }

    private void TryClearMarker()
    {
        try
        {
            File.Delete(AppPaths.FirstRunSetupMarkerFile);
        }
        catch (Exception ex)
        {
            // Non-fatal: a stale marker only means the dialog shows once more.
            _logger.LogWarning(ex, "First-run setup: could not delete the setup marker");
        }
    }
}
