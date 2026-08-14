namespace EntraPimManager.Core.Diagnostics;

/// <summary>
/// All probe results for one group, plus the proxy the app's process would use
/// for it — a WAM-broker-vs-app proxy mismatch is a classic cause of a blank
/// sign-in window, so the report names the app's view explicitly.
/// </summary>
/// <param name="Group">The probed group.</param>
/// <param name="ProxyUri">Proxy URI (credentials stripped) or <c>null</c> for a direct connection.</param>
/// <param name="Results">Per-endpoint results, in catalog order.</param>
public sealed record NetworkGroupResult(
    NetworkProbeGroup Group,
    string? ProxyUri,
    IReadOnlyList<NetworkProbeResult> Results);
