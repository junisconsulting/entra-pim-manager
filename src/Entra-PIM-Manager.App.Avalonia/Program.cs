namespace EntraPimManager.AppAvalonia;

using Avalonia;
using EntraPimManager.AppAvalonia.Services;
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
    /// "Start with Windows" autostart toggle to ON so the admin gets a
    /// zero-config experience: install once, app starts on every login.
    /// The user keeps full control via Settings — Velopack only fires this
    /// hook once per install, so a manual disable sticks.
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
    }
}
