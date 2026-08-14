namespace EntraPimManager.Core.Diagnostics;

/// <summary>
/// One endpoint the app — or the out-of-process Windows sign-in broker (WAM) —
/// must be able to reach. All probes are HTTPS over TCP 443; the app never
/// uses port 80.
/// </summary>
/// <param name="Url">Full probe URL (always <c>https</c>); its host is what UI and report show.</param>
/// <param name="Purpose">Short label naming what breaks when this host is blocked.</param>
public sealed record NetworkProbe(Uri Url, string Purpose);
