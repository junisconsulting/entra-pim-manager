namespace EntraPimManager.AppAvalonia;

using System.IO;
using System.Threading;
using Avalonia;
using EntraPimManager.AppAvalonia.Services;
using EntraPimManager.Core.Configuration;
using Velopack;

/// <summary>
/// Entry point for the Avalonia tray app. Velopack's install/update/uninstall
/// hooks run first so a non-launch invocation (e.g. silent installer) exits
/// before Avalonia spins up the UI thread. A second normal launch is rejected by
/// a single-instance gate so only one tray icon ever exists per user session.
/// </summary>
public static class Program
{
    // Session-scoped (Local namespace) names: one tray instance per interactive
    // login, while a second Windows user on the same machine stays independent.
    private const string SingleInstanceMutexName = "EntraPimManager.SingleInstance";
    private const string ShowWindowSignalName = "EntraPimManager.ShowWindow";

    // Held for the whole process lifetime (static so it isn't garbage-collected,
    // which would release the mutex). The OS releases it when the process exits.
    private static Mutex? _singleInstanceMutex;

    /// <summary>
    /// Auto-reset event the primary instance waits on. A second launch sets it to
    /// ask the already-running instance to surface its tray popup. Null when it
    /// couldn't be created — single-instance still holds, we just can't wake the
    /// window. Read by <see cref="App"/> to start its listener.
    /// </summary>
    public static EventWaitHandle? ShowWindowSignal { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack hooks must run first; hook invocations exit inside Run() before
        // reaching the gate below, so an install/update launch never contends here.
        VelopackApp.Build()
            .OnFirstRun(_ => EnableAutostartOnFirstRun())
            .Run();

        // If we can't take the mutex, another instance owns it: wake its window
        // and bow out without starting a second tray icon.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isPrimaryInstance);
        if (!isPrimaryInstance)
        {
            SignalExistingInstance();
            return 0;
        }

        CreateShowWindowSignal();

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Avalonia application builder. Public so the Avalonia previewer can find it.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Creates the named auto-reset event the running instance listens on. Done
    /// here (before Avalonia starts) so the handle exists by the time any second
    /// launch tries to signal it.
    /// </summary>
    private static void CreateShowWindowSignal()
    {
        try
        {
            ShowWindowSignal = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ShowWindowSignalName);
        }
        catch
        {
            // Non-fatal: the mutex still enforces single-instance; we just can't
            // surface the existing window when a second launch is attempted.
            ShowWindowSignal = null;
        }
    }

    /// <summary>
    /// Best-effort nudge to the already-running instance to show its tray popup.
    /// Called from a second launch right before it exits.
    /// </summary>
    private static void SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowWindowSignalName, out var handle))
            {
                using (handle)
                {
                    handle.Set();
                }
            }
        }
        catch
        {
            // Best effort — the user can still open the running instance from the tray.
        }
    }

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
