namespace EntraPimManager.Core.Auth;

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Lets each distinct MSAL message through once instead of on every token call.
/// </summary>
/// <remarks>
/// MSAL repeats a handful of warnings about its own environment — "Initializing
/// authority from URI … without authority type", "MsaDeviceOperationProvider is not
/// available" — on every single acquisition. With a 60-second refresh across a few
/// enrollments that was twelve lines a minute: a field log came back at 6.5 MB for
/// half a day, of which 99.6 % was those repeats. Real evidence drowns in that, and
/// on a tool whose log is the diagnostic path for an identity admin, drowning the
/// evidence is the actual fault.
/// <para/>
/// They are not suppressed, only deduplicated: the first occurrence is logged in
/// full, so nothing that MSAL reports is lost — it is reported once per process
/// rather than once per call. Errors are never held back.
/// <para/>
/// The dedupe key drops the two parts of MSAL's line header that change on every
/// call, its timestamp and its sequential call id. Everything that identifies the
/// message stays in the key, including the authority URI — so two tenants produce
/// two lines, which is the point.
/// </remarks>
public sealed class MsalLogThrottle
{
    /// <summary>
    /// Ceiling on remembered messages. MSAL puts correlation ids in some messages,
    /// which would otherwise grow this without bound in a tray app that runs for
    /// days. On reaching it the memory is dropped and the first repeats come back —
    /// noisy for one round, never unbounded.
    /// </summary>
    private const int MaxRemembered = 500;

    // Two linear alternatives, no nested quantifier — it cannot backtrack, so it needs
    // no timeout.
    private static readonly Regex PerCallHeader = new(
        @"\[\d{4}-\d\d-\d\d [^\]]*\]|\[MSAL:\d+\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ConcurrentDictionary<string, byte> _seen = new(StringComparer.Ordinal);

    /// <summary>Whether this message should reach the log.</summary>
    /// <param name="level">The level the message was mapped to.</param>
    /// <param name="message">MSAL's raw message.</param>
    public bool ShouldLog(LogLevel level, string? message)
    {
        // An error is never a repeat worth dropping: it belongs to one operation the
        // user is waiting on, not to MSAL's description of the machine.
        if (level >= LogLevel.Error || string.IsNullOrEmpty(message))
        {
            return true;
        }

        if (!_seen.TryAdd(PerCallHeader.Replace(message, string.Empty), 0))
        {
            return false;
        }

        // Checked here rather than on entry: ConcurrentDictionary.Count takes every
        // internal lock, and this branch runs once per new message — eight times in a
        // field log's twelve hours — instead of on all fourteen thousand calls.
        if (_seen.Count > MaxRemembered)
        {
            _seen.Clear();
        }

        return true;
    }
}
