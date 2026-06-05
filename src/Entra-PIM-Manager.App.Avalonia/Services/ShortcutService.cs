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
                return File.Exists(StartMenuShortcutPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not determine Start menu shortcut state");
                return false;
            }
        }
    }

    // …\Microsoft\Windows\Start Menu\Programs\{title}.lnk — the StartMenuRoot
    // location Velopack uses for a per-user install.
    private static string StartMenuShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
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
            var path = StartMenuShortcutPath;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove the Start menu shortcut");
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
