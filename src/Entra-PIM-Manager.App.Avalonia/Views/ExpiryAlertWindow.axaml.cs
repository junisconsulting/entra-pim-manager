namespace EntraPimManager.AppAvalonia.Views;

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

/// <summary>
/// Standalone, always-on-top alert window that surfaces an imminent PIM
/// activation expiry independently of Windows toast delivery — an unpackaged
/// app cannot reliably show toasts, and Focus Assist / Do Not Disturb suppress
/// them anyway. Created once and shown/hidden by
/// <see cref="Tray.ExpiryAlertController"/>. The two buttons are surfaced as
/// plain events so the controller (which owns window orchestration) decides what
/// "Open" and "Dismiss" do.
/// </summary>
public partial class ExpiryAlertWindow : Window
{
    public ExpiryAlertWindow()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user clicks "Open" — the controller shows the main popup.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Raised when the user clicks "Dismiss" — the controller hides the alert.</summary>
    public event EventHandler? DismissRequested;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnOpenClick(object? sender, RoutedEventArgs e)
        => OpenRequested?.Invoke(this, EventArgs.Empty);

    private void OnDismissClick(object? sender, RoutedEventArgs e)
        => DismissRequested?.Invoke(this, EventArgs.Empty);
}
