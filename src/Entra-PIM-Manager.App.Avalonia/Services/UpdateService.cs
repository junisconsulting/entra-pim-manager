namespace EntraPimManager.AppAvalonia.Services;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

/// <summary>
/// Velopack-backed implementation of <see cref="IUpdateService"/>. Reads the
/// release feed from the project's public GitHub Releases (no access token).
/// Outside a Velopack install <see cref="UpdateManager.IsInstalled"/> is false
/// (or construction throws), and every operation degrades to a logged no-op.
/// A release younger than <see cref="MinimumReleaseAge"/> is never offered.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    /// <summary>
    /// Do not offer a release until it is this old. Our releases are unsigned
    /// (see the engineering backlog, "Releases are not code-signed"), and
    /// reputation-based controls in managed environments — field-confirmed:
    /// the Defender ASR "advanced protection against ransomware" rule — block
    /// a fresh hash for roughly a day until the cloud has seen it. Offering a
    /// fresh release there stages a binary that cannot launch.
    /// </summary>
    private static readonly TimeSpan MinimumReleaseAge = TimeSpan.FromHours(72);

    // A raw HttpClient is deliberate: this talks to the GitHub REST API, not
    // Microsoft Graph — the "all Graph through the service layer" rule doesn't
    // apply, and Velopack hits the same host for the feed anyway.
    private static readonly HttpClient GitHubApi = CreateGitHubApiClient();

    private readonly ILogger<UpdateService> _logger;
    private readonly UpdateManager? _manager;
    private readonly Uri? _releaseTagApiBase;

    public UpdateService(string repoUrl, ILogger<UpdateService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoUrl);
        _logger = logger;

        // Build defensively: outside a Velopack install (dev, previewer, the
        // local cross-build) the manager either reports IsInstalled == false
        // or throws — either way we degrade to unsupported.
        try
        {
            _manager = new UpdateManager(new GithubSource(repoUrl, accessToken: null, prerelease: false));

            // https://github.com/{owner}/{repo} → the API endpoint the release
            // age gate reads `published_at` from.
            var ownerRepo = new Uri(repoUrl).AbsolutePath.Trim('/');
            _releaseTagApiBase = new Uri($"https://api.github.com/repos/{ownerRepo}/releases/tags/");
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
            if (info is null)
            {
                return null;
            }

            var version = info.TargetFullRelease.Version.ToString();
            if (!await IsOldEnoughAsync(version, ct).ConfigureAwait(false))
            {
                return null;
            }

            return new UpdateCheckResult(version, info);
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

    private static HttpClient CreateGitHubApiClient()
    {
        var client = new HttpClient();

        // GitHub's API rejects requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Entra-PIM-Manager");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>
    /// True when the release tagged <c>v{version}</c> was published at least
    /// <see cref="MinimumReleaseAge"/> ago. Unknown age counts as too young —
    /// offering a possibly-fresh binary is exactly the failure this gate
    /// prevents, and the next scheduled check retries anyway.
    /// </summary>
    private async Task<bool> IsOldEnoughAsync(string version, CancellationToken ct)
    {
        try
        {
            using var response = await GitHubApi
                .GetAsync(new Uri(_releaseTagApiBase!, "v" + version), ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Release age lookup for {Version} returned HTTP {Status} — deferring the update",
                    version,
                    (int)response.StatusCode);
                return false;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var publishedAt = json.RootElement.GetProperty("published_at").GetDateTimeOffset();
            var age = DateTimeOffset.UtcNow - publishedAt;
            if (age < MinimumReleaseAge)
            {
                _logger.LogInformation(
                    "Update {Version} is {AgeHours:F0}h old — deferred until {MinimumHours}h (reputation window for unsigned releases)",
                    version,
                    age.TotalHours,
                    MinimumReleaseAge.TotalHours);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Release age lookup for {Version} failed — deferring the update", version);
            return false;
        }
    }
}
