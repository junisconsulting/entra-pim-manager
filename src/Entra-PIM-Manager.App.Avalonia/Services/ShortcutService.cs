namespace EntraPimManager.AppAvalonia.Services;

using System;
using Microsoft.Extensions.Logging;
using Velopack.Locators;
using Velopack.Windows;

// Velopack's Shortcuts type is marked [Obsolete] because Velopack now creates the
// Desktop / StartMenuRoot shortcuts automatically at install and removes them at
// uninstall. We deliberately use it anyway: it is the only supported way to let the
// user OPT OUT of (and back into) the Start menu entry at runtime, and it produces a
// shortcut byte-for-byte identical to the installer's — same launcher stub target and
// same AppUserModelId, which the toast notifications rely on. Suppress CS0618 for the
// whole file rather than scatter pragmas around every reference to the type.
#pragma warning disable CS0618

/// <summary>
/// Adds / removes the per-user Start menu shortcut through Velopack's own shortcut
/// API. Only active inside a real Velopack install — see <see cref="IsSupported"/>.
/// </summary>
public sealed class ShortcutService : IShortcutService
{
    // Velopack's per-user Start menu shortcut lands directly under
    // …\Start Menu\Programs (root), matching what the installer creates.
    private const ShortcutLocation Location = ShortcutLocation.StartMenuRoot;

    private readonly ILogger<ShortcutService> _logger;
    private readonly IVelopackLocator? _locator;

    public ShortcutService(ILogger<ShortcutService> logger)
    {
        _logger = logger;
        _locator = ResolveInstalledLocator();
    }

    /// <inheritdoc />
    public bool IsSupported => _locator is not null;

    /// <inheritdoc />
    public bool IsStartMenuShortcutPresent
    {
        get
        {
            if (_locator is null)
            {
                return false;
            }

            try
            {
                var found = new Shortcuts(_locator)
                    .FindShortcuts(_locator.ThisExeRelativePath, Location);
                return found.Count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not determine Start menu shortcut state");
                return false;
            }
        }
    }

    /// <inheritdoc />
    public void EnableStartMenuShortcut()
    {
        if (_locator is null)
        {
            return;
        }

        try
        {
            new Shortcuts(_locator).CreateShortcutForThisExe(Location);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create the Start menu shortcut");
        }
    }

    /// <inheritdoc />
    public void DisableStartMenuShortcut()
    {
        if (_locator is null)
        {
            return;
        }

        try
        {
            new Shortcuts(_locator).RemoveShortcutForThisExe(Location);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove the Start menu shortcut");
        }
    }

    /// <summary>
    /// Returns the current Velopack locator only when the app is running from an
    /// installed (non-portable) Velopack package; otherwise <c>null</c> so the
    /// service degrades to a no-op in dev / portable / cross-build runs.
    /// </summary>
    private static IVelopackLocator? ResolveInstalledLocator()
    {
        try
        {
            if (!VelopackLocator.IsCurrentSet)
            {
                return null;
            }

            var locator = VelopackLocator.Current;
            return locator.CurrentlyInstalledVersion is not null && !locator.IsPortable
                ? locator
                : null;
        }
        catch
        {
            return null;
        }
    }
}
