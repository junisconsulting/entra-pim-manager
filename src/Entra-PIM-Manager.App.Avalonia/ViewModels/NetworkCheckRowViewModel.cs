namespace EntraPimManager.AppAvalonia.ViewModels;

using EntraPimManager.Core.Diagnostics;

/// <summary>
/// Immutable presentation of one probed endpoint in the Settings network
/// check. Results never change after a run, so this is a plain class rather
/// than an <c>ObservableObject</c>; each run replaces the whole list.
/// </summary>
public sealed class NetworkCheckRowViewModel
{
    public NetworkCheckRowViewModel(NetworkProbeResult result)
    {
        Host = result.Probe.Url.Host;
        DetailLabel = $"{result.Probe.Purpose} · {NetworkDiagnosticsReportFormatter.StatusLabel(result)}";
        IsPass = result.Status == NetworkProbeStatus.Reachable;
    }

    /// <summary>Probed host name.</summary>
    public string Host { get; }

    /// <summary>Purpose plus status, same wording as the copyable report.</summary>
    public string DetailLabel { get; }

    /// <summary>Drives the green dot badge; the red counterparts bind <c>!IsPass</c>.</summary>
    public bool IsPass { get; }
}
