namespace EntraPimManager.Core.Diagnostics;

/// <summary>
/// Probes the Microsoft endpoints the app and the Windows sign-in broker need
/// (per configured cloud, plus the update feed) so a customer can tell in one
/// click whether their network blocks a required endpoint.
/// </summary>
public interface INetworkDiagnosticsService
{
    /// <summary>Runs all probes in parallel and returns the assembled report.</summary>
    Task<NetworkDiagnosticsReport> RunAsync(CancellationToken ct = default);
}
