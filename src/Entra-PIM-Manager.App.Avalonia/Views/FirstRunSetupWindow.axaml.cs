namespace EntraPimManager.AppAvalonia.Views;

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

/// <summary>
/// One-time, centred welcome window shown on the first launch after a Velopack
/// install. Offers opt-out of autostart and the Start menu entry. Created and
/// shown by <see cref="Tray.FirstRunSetupController"/>, which owns the apply logic;
/// the button surfaces as a plain event so the controller decides what it does.
/// </summary>
public partial class FirstRunSetupWindow : Window
{
    public FirstRunSetupWindow()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user clicks "Get started" — apply the chosen options.</summary>
    public event EventHandler? ContinueRequested;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnContinueClick(object? sender, RoutedEventArgs e)
        => ContinueRequested?.Invoke(this, EventArgs.Empty);
}
