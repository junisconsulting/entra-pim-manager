namespace EntraPimManager.Core.Configuration;

using System.IO;

/// <summary>
/// Single source of truth for the per-user file locations Entra PIM Manager reads
/// and writes. Everything lives under
/// <c>%LocalAppData%\Entra-PIM-Manager</c> (non-roaming, per-user, no admin
/// rights) — keeping the paths here prevents the same string being re-typed
/// across the host wiring, the token cache, and the Settings UI, which would
/// otherwise drift apart silently when one is changed.
/// </summary>
public static class AppPaths
{
    /// <summary>Root data directory: <c>%LocalAppData%\Entra-PIM-Manager</c>.</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Entra-PIM-Manager");

    /// <summary>Folder holding the rolling Serilog files.</summary>
    public static string LogDirectory { get; } = Path.Combine(DataDirectory, "logs");

    /// <summary>Enrolled-accounts metadata file.</summary>
    public static string AccountsFile { get; } = Path.Combine(DataDirectory, "accounts.json");

    /// <summary>Persisted justification favorites file.</summary>
    public static string FavoritesFile { get; } = Path.Combine(DataDirectory, "favorites.json");

    /// <summary>Persisted user settings file.</summary>
    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");

    /// <summary>User-entered configuration (e.g. ClientId) written by the Settings UI.</summary>
    public static string LocalConfigFile { get; } = Path.Combine(DataDirectory, "appsettings.local.json");

    /// <summary>
    /// Sentinel written by the Velopack <c>OnFirstRun</c> hook to signal that the
    /// one-time first-run setup dialog (autostart / Start menu opt-out) should be
    /// shown on this launch. The dialog deletes it once the choice is applied, so
    /// it never reappears on subsequent starts. A leading dot keeps it visually
    /// grouped apart from the JSON config files in the data directory.
    /// </summary>
    public static string FirstRunSetupMarkerFile { get; } = Path.Combine(DataDirectory, ".setup-pending");
}
