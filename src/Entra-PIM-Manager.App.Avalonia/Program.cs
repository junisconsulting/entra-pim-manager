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
    /// <summary>
    /// Marks a launch as the replacement half of an in-app restart: instead of
    /// bowing out when the (still exiting) old instance holds the mutex, the new
    /// process waits for it. Passed by <c>SettingsPanelViewModel.RestartApp</c>.
    /// </summary>
    public const string RestartArgument = "--restart";

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
        // and bow out without starting a second tray icon. The one exception is
        // a restart handover, where the owner is the old instance mid-shutdown
        // — there we wait for it to die instead of exiting (else the restart
        // ends with no instance running at all).
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isPrimaryInstance);
        if (!isPrimaryInstance && !TryTakeOverAfterRestart(args))
        {
            SignalExistingInstance();
            return 0;
        }

        CreateShowWindowSignal();

        // Rescue data that pre-0.4.0 versions kept in the Velopack install root.
        // Must run before Serilog, the account store or the MSAL cache open files.
        AppPaths.MigrateLegacyDataDirectory();

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Avalonia application builder. Public so the Avalonia previewer can find it.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// On a restart launch, waits for the old instance to exit and release the
    /// single-instance mutex (it never releases explicitly, so ownership arrives
    /// as an abandoned mutex when its process dies). Returns true once this
    /// process owns the mutex and may continue as the primary instance; false
    /// on a normal (non-restart) launch or if the old instance is still alive
    /// after the timeout.
    /// </summary>
    /// <param name="args">The process command-line arguments.</param>
    /// <returns>True when this process now owns the single-instance mutex.</returns>
    private static bool TryTakeOverAfterRestart(string[] args)
    {
        if (Array.IndexOf(args, RestartArgument) < 0)
        {
            return false;
        }

        try
        {
            // ponytail: 10 s covers any realistic shutdown; on timeout we fall
            // back to today's behavior (wake the survivor and exit).
            return _singleInstanceMutex!.WaitOne(TimeSpan.FromSeconds(10));
        }
        catch (AbandonedMutexException)
        {
            // The expected handover: the old process exited while we waited.
            // Abandonment still transfers ownership to us.
            return true;
        }
    }

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
