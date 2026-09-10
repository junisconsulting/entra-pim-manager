namespace EntraPimManager.AppAvalonia.Tray;

using System.Runtime.InteropServices;
using System.Security;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Threading;
using EntraPimManager.AppAvalonia.Services;
using EntraPimManager.AppAvalonia.ViewModels;
using EntraPimManager.AppAvalonia.Views;
using EntraPimManager.Core.Auth;
using Microsoft.Win32;

/// <summary>
/// Owns the single re-usable popup window. The window is created once and
/// shown/hidden on tray-icon interactions. Tries to position the popup near
/// the mouse cursor at the moment of the click — falls back to the default
/// centred placement on positioning failure.
/// </summary>
public sealed class TrayPopupController
{
    private const int PopupMargin = 8;

    /// <summary>Where Windows keeps the light/dark choice that colours the taskbar.</summary>
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // Tray-click toggle guard window. When the user clicks the tray icon while
    // the popup has focus, the focus loss fires Deactivated first → Hide() →
    // then the tray Clicked event arrives. Without a guard, Toggle() would see
    // IsVisible == false and immediately re-show the window. Treat any tray
    // click that lands within this window of a deactivation-hide as "close".
    private static readonly TimeSpan TogglePingPongGuard = TimeSpan.FromMilliseconds(300);

    // Tray indicator states — see UpdateTrayIcon for which one applies when:
    //   Red   — not signed in (no enrolled account, or the session lapsed)
    //   Grey  — signed in, nothing active right now
    //   Amber — an active role is inside its expiry warning window
    //   Green — active roles, none expiring soon
    //
    // Each ships twice, drawn for a light and for a dark taskbar. One fixed
    // palette cannot win this: whatever colour the icon commits to, some user's
    // accent-coloured taskbar has it too. Loaded on demand and kept — eight
    // icons is a handful of KB, and only the UI thread ever touches the map.
    private static readonly Dictionary<string, WindowIcon> IconCache = new(StringComparer.Ordinal);

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

        // Clipboard for the settings network-check report. Wired here because
        // the controller owns both the window (a TopLevel, hence the clipboard)
        // and the shell VM — the settings VM itself stays free of view types.
        _viewModel.SettingsPanel.CopyReportRequested += text =>
            _window.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;

        // The taskbar can change colour under a running app. Avalonia raises this
        // for any OS colour or theme change; which variant that means is then read
        // from the registry, because Avalonia reports the *app* theme, not the
        // system one the taskbar follows.
        if (Application.Current?.PlatformSettings is { } platformSettings)
        {
            platformSettings.ColorValuesChanged += (_, _) => Dispatcher.UIThread.Post(UpdateTrayIcon);
        }

        _viewModel.ActiveCountChanged += (_, _) => UpdateTrayIcon();
        _viewModel.ExpiringChanged += (_, _) => UpdateTrayIcon();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ShellViewModel.IsSignedIn))
            {
                UpdateTrayIcon();
            }
        };

        // App.axaml can only name one file, so it names the dark-taskbar variant —
        // the Windows default. This puts the right one up before the first refresh
        // rather than leaving a light taskbar wrong until something else changes.
        UpdateTrayIcon();
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

    /// <summary>
    /// The icon for one status, in the variant drawn for the current taskbar.
    /// </summary>
    private static WindowIcon Icon(string state)
    {
        var name = $"tray-icon-{state}-{(IsLightTaskbar() ? "onlight" : "ondark")}";
        if (!IconCache.TryGetValue(name, out var icon))
        {
            using var stream = AssetLoader.Open(new Uri($"avares://Entra-PIM-Manager/Assets/{name}.ico"));
            icon = new WindowIcon(stream);
            IconCache[name] = icon;
        }

        return icon;
    }

    /// <summary>
    /// Whether the taskbar is light. Deliberately not the app's own theme:
    /// Windows keeps app mode and system mode apart (<c>AppsUseLightTheme</c> vs
    /// <c>SystemUsesLightTheme</c>), and it is the system one that colours the
    /// taskbar this icon sits in — someone can run a dark app on a light shell.
    /// Anything unreadable falls back to dark, which is the Windows default.
    /// </summary>
    private static bool IsLightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
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
            nextIcon = Icon("red");
            nextTooltip = "Entra PIM Manager — not signed in";
        }
        else if (_viewModel.ActiveCount == 0)
        {
            // Resting state — nothing is wrong, so it must not compete for
            // attention with the amber expiry warning.
            nextIcon = Icon("grey");
            nextTooltip = "Entra PIM Manager — no active roles";
        }
        else if (_viewModel.MostUrgentExpiring is { } urgent)
        {
            // At least one active role is inside the warning window — raise the
            // tray to an attention state and put the live countdown in the tooltip.
            // Amber, not red: the role is still active (the warning window only
            // covers RemainingTime > 0), and red is reserved for "you have no
            // working privilege" so it doesn't read as "already expired".
            nextIcon = Icon("amber");
            var more = _viewModel.ExpiringCount > 1
                ? $" (+{_viewModel.ExpiringCount - 1} more)"
                : string.Empty;
            nextTooltip = $"Entra PIM Manager — {urgent.DisplayName} expires in {urgent.RemainingText}{more}";
        }
        else
        {
            nextIcon = Icon("green");
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
