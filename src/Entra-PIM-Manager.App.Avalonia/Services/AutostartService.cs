namespace EntraPimManager.AppAvalonia.Services;

using System.IO;
using Microsoft.Win32;

/// <summary>
/// Autostart via the per-user <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>
/// registry key — no admin rights, no <c>HKLM</c>, no scheduled task.
/// </summary>
public sealed class AutostartService : IAutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Entra PIM Manager";

    /// <inheritdoc />
    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is not null;
        }
    }

    /// <inheritdoc />
    public void Enable()
    {
        var executablePath = ResolveAutostartTarget();
        if (executablePath is null)
        {
            return;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, $"\"{executablePath}\"");
    }

    /// <inheritdoc />
    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// When the app runs out of a Velopack install the process lives at
    /// <c>&lt;root&gt;\current\Entra-PIM-Manager.exe</c>, while Velopack also places a
    /// stable launcher stub at <c>&lt;root&gt;\Entra-PIM-Manager.exe</c>. The stub
    /// survives updates without its inode changing, so Windows Task Manager's
    /// startup-app metadata cache keeps resolving it to the right Publisher /
    /// FileDescription. Registering the inner <c>current\</c> path instead
    /// causes Task Manager to display the bare filename with an empty
    /// Publisher column. Dev / portable launches don't have a stub one level
    /// up, so we fall back to the process path in that case.
    /// </summary>
    private static string? ResolveAutostartTarget()
    {
        var processPath = Environment.ProcessPath;
        if (processPath is null)
        {
            return null;
        }

        var processDir = Path.GetDirectoryName(processPath);
        if (processDir is null
            || !string.Equals(Path.GetFileName(processDir), "current", System.StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var stubPath = Path.Combine(
            Path.GetDirectoryName(processDir)!,
            Path.GetFileName(processPath));

        return File.Exists(stubPath) ? stubPath : processPath;
    }
}
