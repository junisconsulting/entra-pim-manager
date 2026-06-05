namespace EntraPimManager.AppAvalonia;

using System.IO;
using Avalonia;
using EntraPimManager.AppAvalonia.Services;
using EntraPimManager.Core.Configuration;
using Velopack;

/// <summary>
/// Entry point for the Avalonia tray app. Velopack's install/update/uninstall
/// hooks run first so a non-launch invocation (e.g. silent installer) exits
/// before Avalonia spins up the UI thread.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        VelopackApp.Build()
            .OnFirstRun(_ => EnableAutostartOnFirstRun())
            .Run();

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Avalonia application builder. Public so the Avalonia previewer can find it.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// On the very first launch after a Velopack install, default the
    /// "Start with Windows" autostart toggle to ON (so even if the setup dialog
    /// never runs the app still starts on login) and drop a marker that asks the
    /// UI to show the one-time first-run setup dialog. There the user can opt out
    /// of autostart and the Start menu entry; the dialog applies the choice and
    /// deletes the marker. Velopack fires this hook once per install, so the
    /// dialog is shown exactly once.
    /// </summary>
    private static void EnableAutostartOnFirstRun()
    {
        try
        {
            new AutostartService().Enable();
        }
        catch
        {
            // Defensive: a registry write failure here must NOT prevent the
            // app from starting. The user can still toggle it from Settings
            // later. We can't log (no logger wired this early) — swallow.
        }

        try
        {
            // The UI thread isn't up yet, so we can't show the dialog here —
            // leave a breadcrumb for FirstRunSetupController to pick up.
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(AppPaths.FirstRunSetupMarkerFile, string.Empty);
        }
        catch
        {
            // Non-fatal: without the marker the app simply keeps the defaults
            // (autostart on, Start menu entry kept) and skips the setup dialog.
        }
    }
}
