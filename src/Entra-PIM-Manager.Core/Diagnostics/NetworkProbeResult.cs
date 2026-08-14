namespace EntraPimManager.Core.Diagnostics;

/// <summary>
/// Result of probing one endpoint.
/// </summary>
/// <param name="Probe">The endpoint that was probed.</param>
/// <param name="Status">Classified outcome.</param>
/// <param name="HttpStatusCode">HTTP status code when <see cref="NetworkProbeStatus.Reachable"/>, else <c>null</c>.</param>
/// <param name="Latency">Time until the response headers arrived, or until the failure.</param>
/// <param name="TlsCertIssuer">
/// Issuer of the server certificate captured during the TLS handshake, set only
/// on <see cref="NetworkProbeStatus.TlsFailure"/> — a non-Microsoft issuer is
/// the smoking gun for corporate TLS inspection.
/// </param>
/// <param name="Detail">Short technical detail (socket error name, exception type), never a raw message.</param>
public sealed record NetworkProbeResult(
    NetworkProbe Probe,
    NetworkProbeStatus Status,
    int? HttpStatusCode,
    TimeSpan Latency,
    string? TlsCertIssuer,
    string? Detail);
