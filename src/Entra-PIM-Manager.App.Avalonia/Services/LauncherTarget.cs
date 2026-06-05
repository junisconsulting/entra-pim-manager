namespace EntraPimManager.AppAvalonia.Services;

using System;
using System.IO;

/// <summary>
/// Resolves the stable executable path to register for autostart / Start menu
/// shortcuts. In a Velopack install the running process lives at
/// <c>&lt;root&gt;\current\Entra-PIM-Manager.exe</c>, while Velopack also places a stable
/// launcher stub at <c>&lt;root&gt;\Entra-PIM-Manager.exe</c>. The stub survives updates
/// without its inode changing, so Windows keeps resolving it to the right
/// Publisher / FileDescription and a registered shortcut / Run entry doesn't break
/// after an update. Registering the inner <c>current\</c> path instead causes Task
/// Manager to show the bare filename with an empty Publisher. Dev / portable
/// launches have no stub one level up, so we fall back to the process path.
/// </summary>
internal static class LauncherTarget
{
    /// <summary>The stable launcher path, or <c>null</c> when the process path is unknown.</summary>
    public static string? Resolve()
    {
        var processPath = Environment.ProcessPath;
        if (processPath is null)
        {
            return null;
        }

        var processDir = Path.GetDirectoryName(processPath);
        if (processDir is null
            || !string.Equals(Path.GetFileName(processDir), "current", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var stubPath = Path.Combine(
            Path.GetDirectoryName(processDir)!,
            Path.GetFileName(processPath));

        return File.Exists(stubPath) ? stubPath : processPath;
    }
}
