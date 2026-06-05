namespace EntraPimManager.AppAvalonia.Services;

/// <summary>
/// Checks GitHub Releases for a newer build and drives the Velopack
/// download / apply flow. Every member no-ops gracefully when the app is not
/// running from a Velopack install (dev / <c>dotnet run</c> / the non-Velopack
/// cross-build), so the update path never throws outside a real install.
/// </summary>
public interface IUpdateService
{
    /// <summary>True only inside a real Velopack install — i.e. updates are possible.</summary>
    bool IsSupported { get; }

    /// <summary>The currently running version, or <c>null</c> when unknown / not installed.</summary>
    string? CurrentVersion { get; }

    /// <summary>
    /// Returns the newest available update, or <c>null</c> when already up to
    /// date, unsupported, or the feed is unreachable. Never throws.
    /// </summary>
    Task<UpdateCheckResult?> CheckAsync(CancellationToken ct = default);

    /// <summary>Downloads the update's assets in the background, reporting 0..100 progress.</summary>
    Task DownloadAsync(UpdateCheckResult update, Action<int> onProgress, CancellationToken ct = default);

    /// <summary>Applies the downloaded update and restarts the app immediately.</summary>
    void ApplyAndRestart(UpdateCheckResult update);

    /// <summary>Stages the downloaded update so it is applied the next time the app exits.</summary>
    void ApplyOnNextLaunch(UpdateCheckResult update);
}
