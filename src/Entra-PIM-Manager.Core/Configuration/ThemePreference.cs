namespace EntraPimManager.Core.Configuration;

/// <summary>
/// Theme variant the user wants the app rendered in. <see cref="System"/>
/// follows the Windows OS preference (Avalonia's <c>ThemeVariant.Default</c>),
/// the other two pin the variant regardless of the OS setting.
/// </summary>
public enum ThemePreference
{
    /// <summary>Follow the Windows OS theme (light or dark).</summary>
    System,

    /// <summary>Always render the light theme variant.</summary>
    Light,

    /// <summary>Always render the dark theme variant.</summary>
    Dark,
}
