namespace EntraPimManager.Tests.Diagnostics;

using System.Net.Sockets;
using System.Security.Authentication;
using EntraPimManager.Core.Diagnostics;

/// <summary>
/// Covers the exception → status mapping. The classification is the product:
/// a customer report saying "DNS failure" vs "TLS inspection" decides whether
/// the firewall team or the proxy team gets the ticket.
/// </summary>
public sealed class NetworkProbeClassifierTests
{
    [Fact]
    public void Classify_NameResolutionError_ReturnsDnsFailure()
    {
        var ex = new HttpRequestException(HttpRequestError.NameResolutionError, "resolution failed");

        Assert.Equal(NetworkProbeStatus.DnsFailure, NetworkProbeClassifier.Classify(ex).Status);
    }

    [Fact]
    public void Classify_SecureConnectionErrorWithAuthenticationException_ReturnsTlsFailure()
    {
        var ex = new HttpRequestException(
            HttpRequestError.SecureConnectionError,
            "handshake failed",
            new AuthenticationException("The remote certificate was rejected."));

        Assert.Equal(NetworkProbeStatus.TlsFailure, NetworkProbeClassifier.Classify(ex).Status);
    }

    [Fact]
    public void Classify_ConnectionRefusedSocketError_ReturnsConnectFailure()
    {
        // A firewall that actively resets blocked destinations surfaces as an
        // instant ConnectionRefused — calling that "Timeout" would be wrong.
        var socket = new SocketException((int)SocketError.ConnectionRefused);
        var ex = new HttpRequestException(HttpRequestError.ConnectionError, "connect failed", socket);

        var (status, detail) = NetworkProbeClassifier.Classify(ex);

        Assert.Equal(NetworkProbeStatus.ConnectFailure, status);
        Assert.Equal(nameof(SocketError.ConnectionRefused), detail);
    }

    [Fact]
    public void Classify_TimedOutSocketErrorInConnectionError_ReturnsTimeout()
    {
        var socket = new SocketException((int)SocketError.TimedOut);
        var ex = new HttpRequestException(HttpRequestError.ConnectionError, "connect failed", socket);

        Assert.Equal(NetworkProbeStatus.Timeout, NetworkProbeClassifier.Classify(ex).Status);
    }

    [Fact]
    public void Classify_InnerSocketExceptionHostNotFound_WithoutHttpRequestError_ReturnsDnsFailure()
    {
        // HttpRequestError is only populated by SocketsHttpHandler-originated
        // failures; re-wrapped exceptions report Unknown and must fall back to
        // the inner-exception walk.
        var ex = new InvalidOperationException(
            "wrapped",
            new SocketException((int)SocketError.HostNotFound));

        Assert.Equal(NetworkProbeStatus.DnsFailure, NetworkProbeClassifier.Classify(ex).Status);
    }

    [Fact]
    public void Classify_AuthenticationExceptionDeepInChain_ReturnsTlsFailure()
    {
        var ex = new InvalidOperationException(
            "wrapped",
            new IOException("stream", new AuthenticationException("bad cert")));

        Assert.Equal(NetworkProbeStatus.TlsFailure, NetworkProbeClassifier.Classify(ex).Status);
    }

    [Fact]
    public void Classify_TaskCanceled_ReturnsTimeout()
    {
        var ex = new TaskCanceledException("canceled", new TimeoutException());

        Assert.Equal(NetworkProbeStatus.Timeout, NetworkProbeClassifier.Classify(ex).Status);
    }

    [Fact]
    public void Classify_UnrecognizedException_ReturnsConnectFailureWithTypeName()
    {
        var (status, detail) = NetworkProbeClassifier.Classify(new InvalidOperationException("boom"));

        Assert.Equal(NetworkProbeStatus.ConnectFailure, status);
        Assert.Equal(nameof(InvalidOperationException), detail);
    }
}
