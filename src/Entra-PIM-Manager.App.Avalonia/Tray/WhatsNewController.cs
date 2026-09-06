namespace EntraPimManager.AppAvalonia.Tray;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Threading;
using EntraPimManager.AppAvalonia.Services;
using EntraPimManager.AppAvalonia.ViewModels;
using EntraPimManager.AppAvalonia.Views;
using EntraPimManager.Core.Configuration;
using EntraPimManager.Core.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Shows the release notes once after the version changed.
/// </summary>
/// <remarks>
/// Two triggers, neither of which covers the other — see
/// <see cref="WhatsNewTrigger"/> for the rule and why an installer run counts even
/// when the version string did not change. The
/// notes ship with the build as an embedded resource, so the window works offline —
/// the machines this tool is used on are often the ones behind the strictest
/// proxies. Everything here is best-effort: a missing or unreadable file must never
/// keep the tray from coming up, so the version is recorded either way and the
/// window simply stays away.
/// </remarks>
public sealed class WhatsNewController
{
    private const string ResourcePrefix = "ReleaseNotes.";

    private readonly WhatsNewWindow _window;
    private readonly WhatsNewViewModel _viewModel;
    private readonly IUserSettingsService _userSettings;
    private readonly ILogger<WhatsNewController> _logger;

    public WhatsNewController(
        WhatsNewWindow window,
        WhatsNewViewModel viewModel,
        IUserSettingsService userSettings,
        ILogger<WhatsNewController> logger)
    {
        _window = window;
        _viewModel = viewModel;
        _userSettings = userSettings;
        _logger = logger;
        _window.DataContext = _viewModel;

        _window.Confirmed += (_, _) => _window.Close();
        _window.ReleaseNotesRequested += (_, _) => OpenReleases();
    }

    /// <summary>The running version as <c>Major.Minor.Patch</c>, or <c>null</c> when unknown.</summary>
    public static string? CurrentVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is null ? null : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>Shows the notes after an install or a version change. No-ops otherwise.</summary>
    public void Start()
    {
        // Read before FirstRunSetupController's window is dismissed — that is what
        // deletes the marker. Both Start() calls run back to back on this thread and
        // that window is only posted to the dispatcher, so it is still here.
        var afterInstall = File.Exists(AppPaths.FirstRunSetupMarkerFile);
        if (CurrentVersion() is not { } version
            || !WhatsNewTrigger.ShouldShow(version, _userSettings.Current.LastSeenVersion, afterInstall))
        {
            return;
        }

        // Recorded before the window opens, not after it is confirmed: a user who
        // dismisses it with the title-bar X has still seen it, and a crash in
        // between must not make it reappear on every start.
        Remember(version);

        var notes = ReleaseNotesFormatter.Parse(ReadNotes(version));
        if (notes.Count == 0)
        {
            // Warning, not Information: a build that ships without its own notes is a
            // packing fault, and this line is the only trace it leaves. It has to
            // survive the default log level, which is where the last report of a
            // missing window ran out of evidence.
            _logger.LogWarning("No release notes shipped for version {Version}; skipping the what's-new window", version);
            return;
        }

        _logger.LogInformation(
            "Showing the what's-new window for {Version} (after an install: {AfterInstall})", version, afterInstall);

        _viewModel.VersionText = $"Version {version}";
        _viewModel.Notes.Clear();
        foreach (var note in notes)
        {
            _viewModel.Notes.Add(WhatsNewViewModel.ReleaseNoteItem.From(note));
        }

        Dispatcher.UIThread.Post(() => _window.Show());
    }

    private static string? ReadNotes(string version)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($"{ResourcePrefix}{version}.md", StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // Fire-and-forget with the try/catch *inside* the async method: wrapping the
    // un-awaited call instead would catch nothing and leave an unobserved task
    // exception. Worst case the window shows again next start — never a reason to
    // fail startup.
    private void Remember(string version)
        => _ = RememberAsync(version);

    private async Task RememberAsync(string version)
    {
        try
        {
            await _userSettings
                .SaveAsync(_userSettings.Current with { LastSeenVersion = version })
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record the last seen version");
        }
    }

    private void OpenReleases()
    {
        try
        {
            Process.Start(new ProcessStartInfo($"{ShellViewModel.GitHubProjectUrl}/releases") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open the releases page");
        }
    }
}
