namespace EntraPimManager.Tests.Diagnostics;

using System.Collections.Concurrent;
using System.Net;
using EntraPimManager.Core.Configuration;
using EntraPimManager.Core.Diagnostics;
using EntraPimManager.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// Covers the fan-out orchestration: one broken endpoint must not poison the
/// rest, any HTTP status counts as reachable, and a captured TLS issuer lands
/// on the failing row. Proxy values are never asserted — they depend on the
/// host environment.
/// </summary>
public sealed class NetworkDiagnosticsServiceTests
{
    private const string GlobalId = "8f3a1c2e-0000-4000-8000-000000000001";

    [Fact]
    public async Task RunAsync_OneProbeThrows_OthersStillReport_AndHttp403CountsAsReachable()
    {
        var dns = new HttpRequestException(HttpRequestError.NameResolutionError, "no such host");
        var service = CreateService(request =>
            request.RequestUri!.Host == "aadcdn.msauth.net" ? dns : null);

        var report = await service.RunAsync();

        // Global group plus the update-feed group, catalog order preserved.
        Assert.Equal(2, report.Groups.Count);
        var global = report.Groups[0];
        Assert.Equal("Entra Global", global.Group.DisplayName);

        var failed = Assert.Single(global.Results, r => r.Status != NetworkProbeStatus.Reachable);
        Assert.Equal("aadcdn.msauth.net", failed.Probe.Url.Host);
        Assert.Equal(NetworkProbeStatus.DnsFailure, failed.Status);

        var reachable = global.Results.Where(r => r.Status == NetworkProbeStatus.Reachable).ToList();
        Assert.NotEmpty(reachable);
        Assert.All(reachable, r => Assert.Equal(403, r.HttpStatusCode));
    }

    [Fact]
    public async Task RunAsync_TlsFailure_CarriesCapturedIssuer()
    {
        var service = CreateService(
            request => request.RequestUri!.Host == "aadcdn.msftauth.net"
                ? new HttpRequestException(HttpRequestError.SecureConnectionError, "handshake failed")
                : null,
            issuers => issuers["aadcdn.msftauth.net"] = "CN=CorpProxy CA");

        var report = await service.RunAsync();

        var tls = Assert.Single(report.Groups[0].Results, r => r.Status == NetworkProbeStatus.TlsFailure);
        Assert.Equal("CN=CorpProxy CA", tls.TlsCertIssuer);
    }

    [Fact]
    public async Task RunAsync_NoCloudsConfigured_StillProbesUpdateFeed()
    {
        var service = CreateService(_ => null, options: new EntraPimManagerOptions());

        var report = await service.RunAsync();

        var update = Assert.Single(report.Groups);
        Assert.True(update.Group.IsOptional);
        Assert.All(update.Results, r => Assert.Equal(NetworkProbeStatus.Reachable, r.Status));
    }

    private static NetworkDiagnosticsService CreateService(
        Func<HttpRequestMessage, Exception?> exceptionSelector,
        Action<ConcurrentDictionary<string, string>>? seedIssuers = null,
        EntraPimManagerOptions? options = null)
    {
        options ??= new EntraPimManagerOptions { AppRegistrations = { ["Global"] = GlobalId } };
        return new NetworkDiagnosticsService(
            Options.Create(options),
            NullLogger<NetworkDiagnosticsService>.Instance,
            issuers =>
            {
                seedIssuers?.Invoke(issuers);
                return new ThrowingHttpMessageHandler(exceptionSelector, HttpStatusCode.Forbidden);
            });
    }
}
