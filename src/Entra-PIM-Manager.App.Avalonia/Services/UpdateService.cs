namespace EntraPimManager.AppAvalonia.Services;

using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

/// <summary>
/// Velopack-backed implementation of <see cref="IUpdateService"/>. Reads the
/// release feed from the project's public GitHub Releases (no access token).
/// Outside a Velopack install <see cref="UpdateManager.IsInstalled"/> is false
/// (or construction throws), and every operation degrades to a logged no-op.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    private readonly ILogger<UpdateService> _logger;
    private readonly UpdateManager? _manager;

    public UpdateService(string repoUrl, ILogger<UpdateService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoUrl);
        _logger = logger;

        // Build defensively: outside a Velopack install (dev, previewer, the
        // artifacts/win-x64 cross-build) the manager either reports
        // IsInstalled == false or throws — either way we degrade to unsupported.
        try
        {
            _manager = new UpdateManager(new GithubSource(repoUrl, accessToken: null, prerelease: false));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Velopack UpdateManager could not be created; auto-update disabled");
            _manager = null;
        }
    }

    /// <inheritdoc />
    public bool IsSupported => _manager?.IsInstalled == true;

    /// <inheritdoc />
    public string? CurrentVersion => _manager?.CurrentVersion?.ToString();

    /// <inheritdoc />
    public async Task<UpdateCheckResult?> CheckAsync(CancellationToken ct = default)
    {
        if (!IsSupported)
        {
            _logger.LogDebug("Update check skipped — not a Velopack install");
            return null;
        }

        try
        {
            var info = await _manager!.CheckForUpdatesAsync().ConfigureAwait(false);
            return info is null
                ? null
                : new UpdateCheckResult(info.TargetFullRelease.Version.ToString(), info);
        }
        catch (Exception ex)
        {
            // Network down, GitHub rate-limit, or no feed published yet — never
            // surface this to the user; the next scheduled tick retries.
            _logger.LogWarning(ex, "Update check failed");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DownloadAsync(UpdateCheckResult update, Action<int> onProgress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(onProgress);
        if (!IsSupported)
        {
            return;
        }

        await _manager!.DownloadUpdatesAsync(update.Info, onProgress, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void ApplyAndRestart(UpdateCheckResult update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!IsSupported)
        {
            return;
        }

        // Exits the current process and relaunches into the new version.
        _manager!.ApplyUpdatesAndRestart(update.Info.TargetFullRelease);
    }

    /// <inheritdoc />
    public void ApplyOnNextLaunch(UpdateCheckResult update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!IsSupported)
        {
            return;
        }

        // Staged: the already-downloaded update is applied silently the next
        // time the process exits, without a second download.
        _manager!.WaitExitThenApplyUpdates(update.Info.TargetFullRelease, silent: true, restart: false);
    }
}
