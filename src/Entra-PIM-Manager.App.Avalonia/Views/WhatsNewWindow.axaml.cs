namespace EntraPimManager.AppAvalonia.Views;

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

/// <summary>
/// One-time window listing what changed, shown on the first launch after the
/// version changed. Created and shown by <see cref="Tray.WhatsNewController"/>,
/// which owns the "already seen" bookkeeping; the buttons surface as plain events
/// so the controller decides what they do.
/// </summary>
public partial class WhatsNewWindow : Window
{
    public WhatsNewWindow()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user clicks OK.</summary>
    public event EventHandler? Confirmed;

    /// <summary>Raised when the user asks for the full notes on GitHub.</summary>
    public event EventHandler? ReleaseNotesRequested;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
        => Confirmed?.Invoke(this, EventArgs.Empty);

    private void OnOpenReleaseNotes(object? sender, RoutedEventArgs e)
        => ReleaseNotesRequested?.Invoke(this, EventArgs.Empty);
}
