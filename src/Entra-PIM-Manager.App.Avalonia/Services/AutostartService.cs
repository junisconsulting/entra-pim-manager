namespace EntraPimManager.AppAvalonia.Services;

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
        var executablePath = LauncherTarget.Resolve();
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
}
