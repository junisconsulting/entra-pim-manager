namespace EntraPimManager.AppAvalonia.Services;

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Velopack.Locators;

/// <summary>
/// Adds / removes the per-user Start menu shortcut by writing or deleting the
/// <c>.lnk</c> file directly at the same path Velopack's installer uses
/// (<c>…\Start Menu\Programs\{ProductName}.lnk</c>).
/// <para>
/// We do NOT use Velopack's runtime <c>Shortcuts</c> API: besides being
/// <c>[Obsolete]</c>, its create/delete methods first read the local <c>.nupkg</c>
/// (<c>GetLatestLocalFullPackage</c> + <c>ZipPackage</c>) and bail out silently if
/// that lookup fails, so a Settings toggle appeared to do nothing. Managing the
/// file ourselves is deterministic. The installer's own runtime shortcut helper
/// sets no AppUserModelId either, and the toast stack self-registers via the
/// registry, so writing a plain shortcut here does not affect notifications.
/// </para>
/// Only active inside a real Velopack install — see <see cref="IsSupported"/>.
/// </summary>
public sealed class ShortcutService : IShortcutService
{
    // Must match the installer's shortcut name (vpk --packTitle / the assembly
    // Product) so removal targets exactly the file the installer created.
    private const string FallbackTitle = "Entra PIM Manager";

    // Must match vpk --packAuthors — the subfolder name legacy `--shortcuts
    // StartMenu` builds nested the .lnk under. Used only to clean that up.
    private const string PackAuthors = "junis GmbH";

    private readonly ILogger<ShortcutService> _logger;
    private readonly bool _isInstalled;

    public ShortcutService(ILogger<ShortcutService> logger)
    {
        _logger = logger;
        _isInstalled = ResolveIsInstalled();
    }

    /// <inheritdoc />
    public bool IsSupported => _isInstalled;

    /// <inheritdoc />
    public bool IsStartMenuShortcutPresent
    {
        get
        {
            try
            {
                // A legacy author-subfolder entry counts too, so the Settings
                // toggle shows "on" for installs that predate the root move.
                return File.Exists(StartMenuShortcutPath)
                    || File.Exists(LegacyAuthorSubfolderShortcutPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not determine Start menu shortcut state");
                return false;
            }
        }
    }

    // …\Microsoft\Windows\Start Menu\Programs\{title}.lnk — the StartMenuRoot
    // location the installer (vpk --shortcuts StartMenuRoot) uses for a per-user
    // install, and the canonical path this service creates/removes.
    private static string StartMenuShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            ShortcutTitle + ".lnk");

    // Legacy location from builds that packed with `--shortcuts StartMenu`, which
    // nests the .lnk in a "{packAuthors}" subfolder. Existing installs upgraded
    // from those builds still have the entry here; we clean it up on disable and
    // count it as "present" so the Settings toggle reflects reality.
    private static string LegacyAuthorSubfolderShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            PackAuthors,
            ShortcutTitle + ".lnk");

    // The exe's ProductName (what Velopack derives the shortcut title from);
    // falls back to the known title if version info is unavailable.
    private static string ShortcutTitle
    {
        get
        {
            try
            {
                var processPath = Environment.ProcessPath;
                var productName = processPath is null
                    ? null
                    : FileVersionInfo.GetVersionInfo(processPath).ProductName;
                return string.IsNullOrWhiteSpace(productName) ? FallbackTitle : productName!;
            }
            catch
            {
                return FallbackTitle;
            }
        }
    }

    /// <inheritdoc />
    public void EnableStartMenuShortcut()
    {
        if (!_isInstalled)
        {
            return;
        }

        var target = LauncherTarget.Resolve();
        if (target is null)
        {
            _logger.LogWarning("Cannot create the Start menu shortcut — launcher path unknown");
            return;
        }

        try
        {
            var path = StartMenuShortcutPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteShortcut(path, target);

            // Consolidate to the root location: if an upgraded install still has
            // the legacy author-subfolder entry, drop it so the user isn't left
            // with two identical Start menu shortcuts.
            RemoveLegacyAuthorSubfolderShortcut();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create the Start menu shortcut");
        }
    }

    /// <inheritdoc />
    public void DisableStartMenuShortcut()
    {
        if (!_isInstalled)
        {
            return;
        }

        try
        {
            if (File.Exists(StartMenuShortcutPath))
            {
                File.Delete(StartMenuShortcutPath);
            }

            RemoveLegacyAuthorSubfolderShortcut();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove the Start menu shortcut");
        }
    }

    /// <summary>
    /// Deletes the legacy author-subfolder <c>.lnk</c> (from builds packed with
    /// <c>--shortcuts StartMenu</c>) and the now-empty subfolder, if present.
    /// No-op when there is nothing to clean up.
    /// </summary>
    private static void RemoveLegacyAuthorSubfolderShortcut()
    {
        if (!File.Exists(LegacyAuthorSubfolderShortcutPath))
        {
            return;
        }

        File.Delete(LegacyAuthorSubfolderShortcutPath);
        var legacyDir = Path.GetDirectoryName(LegacyAuthorSubfolderShortcutPath)!;
        if (Directory.Exists(legacyDir) && Directory.GetFileSystemEntries(legacyDir).Length == 0)
        {
            Directory.Delete(legacyDir);
        }
    }

    /// <summary>
    /// Writes a <c>.lnk</c> via the Windows Script Host shell object (late-bound, so
    /// no COM interface declarations are needed). Points at the stable launcher stub
    /// with the exe's own icon.
    /// </summary>
    private static void WriteShortcut(string linkPath, string targetPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell is not registered.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(linkPath);
            try
            {
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                shortcut.IconLocation = targetPath + ",0";
                shortcut.Description = FallbackTitle;
                shortcut.Save();
            }
            finally
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>
    /// True only when running from an installed (non-portable) Velopack package;
    /// otherwise the service no-ops and the Settings row hides.
    /// </summary>
    private static bool ResolveIsInstalled()
    {
        try
        {
            if (!VelopackLocator.IsCurrentSet)
            {
                return false;
            }

            var locator = VelopackLocator.Current;
            return locator.CurrentlyInstalledVersion is not null && !locator.IsPortable;
        }
        catch
        {
            return false;
        }
    }
}
