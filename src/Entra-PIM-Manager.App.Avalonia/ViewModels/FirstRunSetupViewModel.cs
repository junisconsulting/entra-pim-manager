namespace EntraPimManager.AppAvalonia.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>
/// Backing model for the one-time first-run setup window. The two toggles default
/// to ON so the recommended zero-config experience is a single click on "Get
/// started"; <see cref="Tray.FirstRunSetupController"/> seeds them from the actual
/// install state and applies the result. UI text is English per project conventions.
/// </summary>
public sealed partial class FirstRunSetupViewModel : ObservableObject
{
    /// <summary>Whether the app should launch automatically when the user signs in.</summary>
    [ObservableProperty]
    private bool _startWithWindows = true;

    /// <summary>Whether to keep the Start menu shortcut Velopack created at install.</summary>
    [ObservableProperty]
    private bool _createStartMenuShortcut = true;

    /// <summary>
    /// Whether the Start menu row is shown at all. False outside a Velopack install
    /// (dev / portable), where there is no shortcut to manage.
    /// </summary>
    [ObservableProperty]
    private bool _canManageStartMenuShortcut = true;
}
