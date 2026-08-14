namespace EntraPimManager.Core.Diagnostics;

/// <summary>
/// Outcome of a single endpoint probe. Any HTTP response — including 4xx —
/// counts as <see cref="Reachable"/>; the failure states classify what went
/// wrong below the HTTP layer.
/// </summary>
public enum NetworkProbeStatus
{
    /// <summary>An HTTP response arrived; the endpoint is reachable.</summary>
    Reachable,

    /// <summary>The host name did not resolve — DNS is blocked or filtered.</summary>
    DnsFailure,

    /// <summary>
    /// The TCP connection was refused or the network is unreachable — typical
    /// for a firewall that actively resets blocked destinations.
    /// </summary>
    ConnectFailure,

    /// <summary>
    /// No response within the per-probe timeout — typical for a firewall that
    /// silently drops packets. Covers both connect and response timeouts.
    /// </summary>
    Timeout,

    /// <summary>
    /// The TLS handshake failed — with a corporate proxy in the path this is
    /// usually TLS inspection; the captured certificate issuer names it.
    /// </summary>
    TlsFailure,
}
