namespace EntraPimManager.AppAvalonia.Tray;

using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using EntraPimManager.AppAvalonia.ViewModels;
using EntraPimManager.AppAvalonia.Views;

/// <summary>
/// Owns the single re-usable expiry-alert window. Observes
/// <see cref="ShellViewModel.IsExpiryAlertVisible"/> and shows / hides a small
/// always-on-top window anchored to the bottom-right of the primary work area
/// (just above the tray) when an active assignment enters the user-configured
/// warning window. Because this is our own window rather than a system toast,
/// it stays visible through Focus Assist / Do Not Disturb — which is exactly
/// why the original toast-only warning was never seen on Windows 11.
/// </summary>
public sealed class ExpiryAlertController
{
    private const int ScreenMargin = 12;

    // Logical-height estimate used to anchor the window before its first layout
    // pass populates the real Bounds; corrected immediately afterwards so any
    // residual jump is sub-pixel.
    private const double EstimatedHeight = 188;

    private readonly ExpiryAlertWindow _window;
    private readonly ShellViewModel _viewModel;
    private readonly TrayPopupController _popupController;
    private bool _allowClose;

    public ExpiryAlertController(
        ExpiryAlertWindow window,
        ShellViewModel viewModel,
        TrayPopupController popupController)
    {
        _window = window;
        _viewModel = viewModel;
        _popupController = popupController;
        _window.DataContext = _viewModel.ExpiryAlert;

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

        // "Open" surfaces the main popup and dismisses the alert; "Dismiss" just
        // suppresses the alert for this assignment until it re-enters the window.
        _window.OpenRequested += (_, _) =>
        {
            _popupController.Show();
            _viewModel.DismissCurrentExpiryAlert();
        };
        _window.DismissRequested += (_, _) => _viewModel.DismissCurrentExpiryAlert();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>Allows the next window-close to actually close (called on app exit).</summary>
    public void PrepareForShutdown() => _allowClose = true;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.IsExpiryAlertVisible))
        {
            return;
        }

        if (_viewModel.IsExpiryAlertVisible)
        {
            Show();
        }
        else
        {
            Hide();
        }
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
