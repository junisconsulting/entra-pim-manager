namespace EntraPimManager.AppAvalonia.Services;

using Avalonia.Styling;
using EntraPimManager.Core.Configuration;

/// <summary>
/// Maps the persisted <see cref="ThemePreference"/> to the Avalonia
/// <see cref="ThemeVariant"/> the application root expects.
/// <see cref="ThemePreference.System"/> resolves to <see cref="ThemeVariant.Default"/>,
/// which lets Avalonia follow the Windows OS theme.
/// </summary>
internal static class ThemeMapper
{
    public static ThemeVariant ToVariant(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => ThemeVariant.Light,
        ThemePreference.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}
