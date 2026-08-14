namespace EntraPimManager.Core.Diagnostics;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using EntraPimManager.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Default <see cref="INetworkDiagnosticsService"/>: fans out over every
/// catalog probe with a per-probe timeout, classifies failures, captures the
/// TLS certificate issuer for inspection detection, and reports the proxy the
/// app's process would use per group.
/// </summary>
/// <remarks>
/// A raw <see cref="HttpClient"/> is deliberate here: these are anonymous
/// reachability probes, not Graph calls, so the "all Graph access goes through
/// the service layer" rule does not apply. One client per run — this is a
/// manual one-click diagnostic, so connection pooling is irrelevant.
/// </remarks>
public sealed class NetworkDiagnosticsService : INetworkDiagnosticsService
{
    private static readonly TimeSpan PerProbeTimeout = TimeSpan.FromSeconds(10);

    private readonly EntraPimManagerOptions _options;
    private readonly ILogger<NetworkDiagnosticsService> _logger;
    private readonly Func<ConcurrentDictionary<string, string>, HttpMessageHandler>? _handlerFactory;

    public NetworkDiagnosticsService(
        IOptions<EntraPimManagerOptions> options,
        ILogger<NetworkDiagnosticsService> logger)
        : this(options, logger, handlerFactory: null)
    {
    }

    /// <summary>
    /// Test seam: lets unit tests replace the live TLS-probing handler. The
    /// factory receives the per-run issuer sink the real handler writes into.
    /// </summary>
    internal NetworkDiagnosticsService(
        IOptions<EntraPimManagerOptions> options,
        ILogger<NetworkDiagnosticsService> logger,
        Func<ConcurrentDictionary<string, string>, HttpMessageHandler>? handlerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger;
        _handlerFactory = handlerFactory;
    }

    /// <inheritdoc />
    public async Task<NetworkDiagnosticsReport> RunAsync(CancellationToken ct = default)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var groups = NetworkProbeCatalog.GroupsFor(_options.ConfiguredClouds());

        // Server-certificate issuer per host, captured during the TLS handshake
        // so corporate TLS inspection shows up in the report by CA name.
        var issuers = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var handler = _handlerFactory?.Invoke(issuers) ?? CreateProbeHandler(issuers);
        using var client = new HttpClient(handler);

        var groupTasks = groups.Select(async group =>
        {
            var results = await Task
                .WhenAll(group.Probes.Select(probe => ProbeAsync(client, probe, issuers, ct)))
                .ConfigureAwait(false);
            return new NetworkGroupResult(group, ProxyFor(group), results);
        });

        var groupResults = await Task.WhenAll(groupTasks).ConfigureAwait(false);
        return new NetworkDiagnosticsReport(timestamp, groupResults);
    }

    private static string? ProxyFor(NetworkProbeGroup group)
    {
        var url = group.Probes[0].Url;
        var defaultProxy = HttpClient.DefaultProxy;
        if (defaultProxy.IsBypassed(url))
        {
            return null;
        }

        var proxy = defaultProxy.GetProxy(url);
        if (proxy is null || proxy == url)
        {
            return null;
        }

        // Strip credentials an HTTP_PROXY value may embed — the report is
        // meant to be pasted into support tickets.
        var builder = new UriBuilder(proxy) { UserName = string.Empty, Password = string.Empty };
        return builder.Uri.ToString();
    }

    /// <summary>
    /// Live probe handler: no redirects (a redirect already proves
    /// reachability), and a certificate callback that records the issuer while
    /// keeping the default validation verdict — observe, never weaken.
    /// </summary>
    /// <remarks>
    /// Excluded from coverage: wires a live TLS validation callback that no
    /// unit test can exercise; verified via the network-check entry in
    /// <c>.claude/manual-test-checklist.md</c>.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    private static HttpClientHandler CreateProbeHandler(ConcurrentDictionary<string, string> issuers)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        handler.ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
        {
            if (certificate is not null && request.RequestUri is not null)
            {
                issuers[request.RequestUri.Host] = certificate.Issuer;
            }

            return errors == SslPolicyErrors.None;
        };
        return handler;
    }

    private async Task<NetworkProbeResult> ProbeAsync(
        HttpClient client,
        NetworkProbe probe,
        ConcurrentDictionary<string, string> issuers,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(PerProbeTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, probe.Url);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);
            stopwatch.Stop();

            // Any HTTP response proves reachability — CDN roots legitimately
            // answer 400/403 to a bare anonymous GET.
            return new NetworkProbeResult(
                probe,
                NetworkProbeStatus.Reachable,
                (int)response.StatusCode,
                stopwatch.Elapsed,
                TlsCertIssuer: null,
                Detail: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            var (status, detail) = NetworkProbeClassifier.Classify(ex);
            var certIssuer = status == NetworkProbeStatus.TlsFailure && issuers.TryGetValue(probe.Url.Host, out var issuer)
                ? issuer
                : null;
            _logger.LogWarning(ex, "Network probe failed for {Host}: {Status}", probe.Url.Host, status);
            return new NetworkProbeResult(probe, status, HttpStatusCode: null, stopwatch.Elapsed, certIssuer, detail);
        }
    }
}
