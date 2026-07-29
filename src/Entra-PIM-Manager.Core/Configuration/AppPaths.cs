namespace EntraPimManager.Core.Configuration;

using System.IO;

/// <summary>
/// Single source of truth for the per-user file locations Entra PIM Manager reads
/// and writes. Everything lives under
/// <c>%LocalAppData%\junis\Entra-PIM-Manager</c> (non-roaming, per-user, no admin
/// rights) — keeping the paths here prevents the same string being re-typed
/// across the host wiring, the token cache, and the Settings UI, which would
/// otherwise drift apart silently when one is changed.
/// <para/>
/// The vendor subfolder is deliberate: <c>%LocalAppData%\Entra-PIM-Manager</c>
/// (without vendor) is Velopack's INSTALL root for this app, which the Setup
/// installer deletes wholesale on a full reinstall. Data stored there is wiped
/// on every Setup-driven update, and an open file (e.g. the rolling log) makes
/// that deletion fail with "Failed to remove existing application directory".
/// See <see cref="MigrateLegacyDataDirectory"/>.
/// </summary>
public static class AppPaths
{
    /// <summary>Root data directory: <c>%LocalAppData%\junis\Entra-PIM-Manager</c>.</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "junis",
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

    /// <summary>
    /// One-time rescue for data written by versions before 0.4.0, which kept all
    /// files directly in <c>%LocalAppData%\Entra-PIM-Manager</c> — Velopack's
    /// install root. Moves the known data items into <see cref="DataDirectory"/>.
    /// Must run at startup before anything opens a file (Serilog, account store,
    /// MSAL cache). Best effort per item: a locked or missing file must never
    /// prevent the app from starting.
    /// </summary>
    public static void MigrateLegacyDataDirectory() => MigrateDataDirectory(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Entra-PIM-Manager"),
        DataDirectory);

    /// <summary>Testable core of <see cref="MigrateLegacyDataDirectory"/>.</summary>
    internal static void MigrateDataDirectory(string legacyRoot, string targetRoot)
    {
        // The legacy root doubles as the Velopack install root, so it exists on
        // every installed machine forever — the per-item existence checks below
        // make this a cheap no-op once the data files have been moved.
        if (!Directory.Exists(legacyRoot)
            || string.Equals(legacyRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Frozen snapshot of the pre-0.4.0 file layout — intentionally literal,
        // independent of what current code names its files.
        string[] legacyFiles =
        [
            "accounts.json",
            "favorites.json",
            "settings.json",
            "appsettings.local.json",
            "msal.cache",
            ".setup-pending",
        ];

        Directory.CreateDirectory(targetRoot);

        foreach (var name in legacyFiles)
        {
            try
            {
                var source = Path.Combine(legacyRoot, name);
                var target = Path.Combine(targetRoot, name);
                if (File.Exists(source) && !File.Exists(target))
                {
                    File.Move(source, target);
                }
            }
            catch (IOException)
            {
                // Locked or unreadable — leave it behind; the app starts with
                // defaults for that item rather than not starting at all.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as above.
            }
        }

        try
        {
            var legacyLogs = Path.Combine(legacyRoot, "logs");
            var targetLogs = Path.Combine(targetRoot, "logs");
            if (Directory.Exists(legacyLogs) && !Directory.Exists(targetLogs))
            {
                Directory.Move(legacyLogs, targetLogs);
            }
        }
        catch (IOException)
        {
            // A previous instance may still hold the current log file open.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}
