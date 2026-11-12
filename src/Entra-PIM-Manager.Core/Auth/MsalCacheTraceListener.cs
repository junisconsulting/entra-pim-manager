namespace EntraPimManager.Core.Auth;

using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

/// <summary>
/// Forwards the MSAL cache extension's <see cref="TraceSource"/> output to the app log.
/// </summary>
/// <remarks>
/// The extension reports a cache file it cannot read or decrypt only there ("Could not read
/// the token cache. Ignoring.") and carries on with an empty cache. Without this the app
/// only sees "no account" and the log never says why — issue #13, where the cache came up
/// empty after every logon on a pooled host, cause not yet confirmed. The trace carries file
/// paths, byte counts and exception texts, never cache content.
/// </remarks>
public sealed class MsalCacheTraceListener : TraceListener
{
    private readonly ILogger _logger;

    public MsalCacheTraceListener(ILogger logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
        => _logger.Log(LevelFor(eventType), "[MSAL cache] {Message}", message);

    /// <inheritdoc />
    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
        => TraceEvent(eventCache, source, eventType, id, format is null || args is null ? format : string.Format(CultureInfo.InvariantCulture, format, args));

    /// <inheritdoc />
    public override void Write(string? message) => _logger.LogDebug("[MSAL cache] {Message}", message);

    /// <inheritdoc />
    public override void WriteLine(string? message) => _logger.LogDebug("[MSAL cache] {Message}", message);

    // Warning and worse is what explains a lost sign-in; the rest is per-access chatter.
    private static LogLevel LevelFor(TraceEventType type)
        => type <= TraceEventType.Warning ? LogLevel.Warning : LogLevel.Debug;
}
