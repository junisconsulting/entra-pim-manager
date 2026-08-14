namespace EntraPimManager.Core.Diagnostics;

using System.Globalization;
using System.Text;

/// <summary>
/// Renders a <see cref="NetworkDiagnosticsReport"/> as the plain-text report a
/// customer pastes into a support ticket. English, no PII: hostnames,
/// statuses, latencies, versions, and a credential-stripped proxy URI only.
/// </summary>
public static class NetworkDiagnosticsReportFormatter
{
    /// <summary>
    /// Fixed caveat shown in the UI and appended to the report. Green cannot
    /// fully vouch for the out-of-process broker, but red is proof of blocking.
    /// </summary>
    public const string WamCaveat =
        "Note: the Windows sign-in window (WAM) runs out-of-process; a green result here "
        + "does not fully guarantee its own requests succeed, but a red row is definitive "
        + "evidence the endpoint is blocked from this machine.";

    /// <summary>Short status text for one result, shared by the report and the settings UI.</summary>
    public static string StatusLabel(NetworkProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Status switch
        {
            NetworkProbeStatus.Reachable => string.Format(
                CultureInfo.InvariantCulture,
                "HTTP {0} · {1} ms",
                result.HttpStatusCode,
                (int)result.Latency.TotalMilliseconds),
            NetworkProbeStatus.DnsFailure => "DNS failure",
            NetworkProbeStatus.Timeout => "Timeout (connect or response)",
            NetworkProbeStatus.TlsFailure => result.TlsCertIssuer is null
                ? "TLS failure — likely TLS inspection"
                : $"TLS failure — likely TLS inspection (issuer: {result.TlsCertIssuer})",
            _ => result.Detail is null
                ? "Blocked (connection failed)"
                : $"Blocked ({result.Detail})",
        };
    }

    /// <summary>Formats the full report.</summary>
    public static string Format(NetworkDiagnosticsReport report, string appVersion, string osVersion)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sb = new StringBuilder();
        sb.AppendLine("Entra PIM Manager — network check");
        sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "Generated: {0:u} · App v{1} · {2}",
            report.TimestampUtc,
            appVersion,
            osVersion));
        sb.AppendLine("All probes HTTPS/443. Any HTTP response (including 4xx) counts as reachable.");

        foreach (var group in report.Groups)
        {
            sb.AppendLine();
            var optional = group.Group.IsOptional ? " (optional)" : string.Empty;
            var proxy = group.ProxyUri ?? "none";
            sb.AppendLine($"== {group.Group.DisplayName}{optional} ==  proxy: {proxy}");

            var hostWidth = group.Results.Max(r => r.Probe.Url.Host.Length) + 2;
            var purposeWidth = group.Results.Max(r => r.Probe.Purpose.Length) + 2;
            foreach (var result in group.Results)
            {
                var marker = result.Status == NetworkProbeStatus.Reachable ? "[OK]  " : "[FAIL]";
                sb.AppendLine(
                    $"  {marker} {result.Probe.Url.Host.PadRight(hostWidth)}{result.Probe.Purpose.PadRight(purposeWidth)}{StatusLabel(result)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine(WamCaveat);
        return sb.ToString();
    }
}
