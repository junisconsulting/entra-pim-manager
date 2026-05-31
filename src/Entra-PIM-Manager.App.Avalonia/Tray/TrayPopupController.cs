namespace EntraPimManager.AppAvalonia.Tray;

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using EntraPimManager.AppAvalonia.Services;
using EntraPimManager.AppAvalonia.ViewModels;
using EntraPimManager.AppAvalonia.Views;
using EntraPimManager.Core.Auth;

/// <summary>
/// Owns the single re-usable popup window. The window is created once and
/// shown/hidden on tray-icon interactions. Tries to position the popup near
/// the mouse cursor at the moment of the click — falls back to the default
/// centred placement on positioning failure.
/// </summary>
public sealed class TrayPopupController
{
    private const int PopupMargin = 8;

    // Tray-click toggle guard window. When the user clicks the tray icon while
    // the popup has focus, the focus loss fires Deactivated first → Hide() →
    // then the tray Clicked event arrives. Without a guard, Toggle() would see
    // IsVisible == false and immediately re-show the window. Treat any tray
    // click that lands within this window of a deactivation-hide as "close".
    private static readonly TimeSpan TogglePingPongGuard = TimeSpan.FromMilliseconds(300);

    // Three-state tray indicator:
    //   Red   — not signed in (no enrolled account / session lapsed)
    //   Amber — signed in but no active PIM role right now
    //   Green — at least one active PIM role across all enrolled accounts
    private static readonly WindowIcon RedIcon = LoadIcon("avares://Entra-PIM-Manager/Assets/tray-icon-red.ico");
    private static readonly WindowIcon AmberIcon = LoadIcon("avares://Entra-PIM-Manager/Assets/tray-icon-amber.ico");
    private static readonly WindowIcon GreenIcon = LoadIcon("avares://Entra-PIM-Manager/Assets/tray-icon-green.ico");

    private readonly TrayPopupWindow _window;
    private readonly ShellViewModel _viewModel;
    private bool _initialized;
    private bool _allowClose;

    public TrayPopupController(
        TrayPopupWindow window,
        ShellViewModel viewModel,
        IWindowTracker windowTracker)
    {
        _window = window;
        _viewModel = viewModel;
        _window.DataContext = _viewModel;

        if (windowTracker is AvaloniaWindowTracker tracker)
        {
            tracker.Track(_window);
        }

        // Hide instead of closing on the OS close action — the tray icon stays alive.
        _window.Closing += (_, args) =>
        {
            if (_allowClose)
            {
                return;
            }

            args.Cancel = true;
            _window.Hide();
        };

        _viewModel.ActiveCountChanged += (_, _) => UpdateTrayIcon();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ShellViewModel.IsSignedIn))
            {
                UpdateTrayIcon();
            }
        };
    }

    /// <summary>Allows the next window-close to actually close (called on app exit).</summary>
    public void PrepareForShutdown() => _allowClose = true;

    /// <summary>Show the popup if hidden, hide it if visible.</summary>
    public void Toggle()
    {
        // The Deactivated handler on the popup fires before the tray Clicked
        // event when the user clicks the tray icon while the popup is open,
        // so by the time we get here IsVisible is already false. Treat a
        // tray click that lands within the guard window as the user
        // explicitly closing the popup — don't immediately re-show.
        if (DateTimeOffset.UtcNow - _window.LastHiddenByDeactivation < TogglePingPongGuard)
        {
            return;
        }

        if (_window.IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    /// <summary>Show the popup positioned near the mouse cursor, then activate it.</summary>
    public void Show()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (!_initialized)
            {
                _initialized = true;
                await _viewModel.InitializeAsync();
                UpdateTrayIcon();
            }

            PositionAtCursor();
            _window.Show();
            _window.Activate();
        });
    }

    /// <summary>Hide the popup; the tray icon remains visible.</summary>
    public void Hide() => Dispatcher.UIThread.Post(_window.Hide);

    /// <summary>Triggers a refresh on the underlying view model.</summary>
    public Task RefreshAsync() => _viewModel.RefreshCommand.ExecuteAsync(null);

    private static WindowIcon LoadIcon(string uri)
    {
        using var stream = AssetLoader.Open(new Uri(uri));
        return new WindowIcon(stream);
    }

    private static bool TryGetCursorPos(out POINT pt)
    {
        if (GetCursorPos(out pt))
        {
            return true;
        }

        pt = default;
        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private void UpdateTrayIcon()
    {
        if (Application.Current is null)
        {
            return;
        }

        var icons = TrayIcon.GetIcons(Application.Current);
        if (icons is null || icons.Count == 0)
        {
            return;
        }

        WindowIcon nextIcon;
        string nextTooltip;
        if (!_viewModel.IsSignedIn)
        {
            // "Not signed in" covers both the cold-start case (no accounts
            // enrolled yet) and an expired session — both surface as
            // IsSignedIn=false on the view model.
            nextIcon = RedIcon;
            nextTooltip = "Entra PIM Manager — not signed in";
        }
        else if (_viewModel.ActiveCount == 0)
        {
            nextIcon = AmberIcon;
            nextTooltip = "Entra PIM Manager — no active roles";
        }
        else
        {
            nextIcon = GreenIcon;
            nextTooltip = $"Entra PIM Manager — {_viewModel.ActiveCount} active role(s)";
        }

        var icon = icons[0];
        icon.Icon = nextIcon;
        icon.ToolTipText = nextTooltip;
    }

    private void PositionAtCursor()
    {
        if (!TryGetCursorPos(out var pt))
        {
            return;
        }

        // Place the popup so its bottom-right corner sits a few px above-left
        // of the cursor (the tray icon is usually in the lower-right corner).
        // If the popup would run off-screen the screen-bounds clamp keeps it visible.
        var screen = _window.Screens.ScreenFromPoint(new PixelPoint(pt.X, pt.Y))
            ?? _window.Screens.Primary;
        var bounds = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);

        var scale = _window.DesktopScaling;
        var width = (int)(_window.Width * scale);
        var height = (int)(_window.Height * scale);

        var x = pt.X - width - PopupMargin;
        var y = pt.Y - height - PopupMargin;

        x = Math.Max(bounds.X, Math.Min(x, bounds.Right - width));
        y = Math.Max(bounds.Y, Math.Min(y, bounds.Bottom - height));

        _window.Position = new PixelPoint(x, y);
        _window.WindowStartupLocation = WindowStartupLocation.Manual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
