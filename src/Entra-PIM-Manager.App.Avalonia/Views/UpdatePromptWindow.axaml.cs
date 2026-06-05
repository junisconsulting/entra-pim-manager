namespace EntraPimManager.AppAvalonia.Views;

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

/// <summary>
/// Standalone, always-on-top window that offers an available update and tracks
/// the download / ready / error stages. Created once and shown / hidden by
/// <see cref="Tray.UpdateController"/>. The buttons surface as plain events so
/// the controller (which owns the Velopack flow) decides what they do.
/// </summary>
public partial class UpdatePromptWindow : Window
{
    public UpdatePromptWindow()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user clicks "Install" — start the background download.</summary>
    public event EventHandler? InstallRequested;

    /// <summary>Raised when the user clicks "Restart now" — apply + relaunch.</summary>
    public event EventHandler? RestartRequested;

    /// <summary>Raised when the user clicks "Later" / "Dismiss" — defer or stage for next launch.</summary>
    public event EventHandler? LaterRequested;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnInstallClick(object? sender, RoutedEventArgs e)
        => InstallRequested?.Invoke(this, EventArgs.Empty);

    private void OnRestartClick(object? sender, RoutedEventArgs e)
        => RestartRequested?.Invoke(this, EventArgs.Empty);

    private void OnLaterClick(object? sender, RoutedEventArgs e)
        => LaterRequested?.Invoke(this, EventArgs.Empty);
}
