namespace EntraPimManager.Core.Diagnostics;

using System.Net.Sockets;
using System.Security.Authentication;

/// <summary>
/// Maps the exception a failed HTTPS probe threw to a
/// <see cref="NetworkProbeStatus"/> plus a short technical detail.
/// </summary>
/// <remarks>
/// <see cref="HttpRequestException.HttpRequestError"/> is authoritative but
/// only populated for failures originating in <c>SocketsHttpHandler</c>;
/// anything re-wrapped reports <see cref="HttpRequestError.Unknown"/>, so the
/// inner-exception walk (same shape as <c>PimErrorMapper.IsNetworkFailure</c>)
/// is a required fallback, not defensive garnish.
/// </remarks>
public static class NetworkProbeClassifier
{
    /// <summary>Classifies <paramref name="exception"/> into a probe status and optional detail.</summary>
    public static (NetworkProbeStatus Status, string? Detail) Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // Includes TaskCanceledException — the per-probe linked CTS fired.
        if (exception is OperationCanceledException)
        {
            return (NetworkProbeStatus.Timeout, null);
        }

        if (exception is HttpRequestException http)
        {
            switch (http.HttpRequestError)
            {
                case HttpRequestError.NameResolutionError:
                    return (NetworkProbeStatus.DnsFailure, null);
                case HttpRequestError.SecureConnectionError:
                    return (NetworkProbeStatus.TlsFailure, null);
                case HttpRequestError.ConnectionError:
                    return ClassifyByInnerChain(http);
            }
        }

        return ClassifyByInnerChain(exception);
    }

    private static (NetworkProbeStatus Status, string? Detail) ClassifyByInnerChain(Exception exception)
    {
        for (Exception? ex = exception; ex is not null; ex = ex.InnerException)
        {
            switch (ex)
            {
                case SocketException socket:
                    return socket.SocketErrorCode is SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData
                        ? (NetworkProbeStatus.DnsFailure, null)
                        : ClassifySocket(socket);
                case AuthenticationException:
                    return (NetworkProbeStatus.TlsFailure, null);
                case TimeoutException:
                    return (NetworkProbeStatus.Timeout, null);
            }
        }

        return (NetworkProbeStatus.ConnectFailure, exception.GetType().Name);
    }

    private static (NetworkProbeStatus Status, string? Detail) ClassifySocket(SocketException? socket)
    {
        if (socket is null)
        {
            return (NetworkProbeStatus.ConnectFailure, null);
        }

        return socket.SocketErrorCode == SocketError.TimedOut
            ? (NetworkProbeStatus.Timeout, null)
            : (NetworkProbeStatus.ConnectFailure, socket.SocketErrorCode.ToString());
    }
}
