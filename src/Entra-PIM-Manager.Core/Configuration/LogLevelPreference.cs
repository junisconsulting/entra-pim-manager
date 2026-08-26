namespace EntraPimManager.Core.Configuration;

/// <summary>
/// Minimum log level the user wants written to the rolling log files,
/// selectable in Settings → Diagnostics. <see cref="Information"/> is the
/// default: the app's own activity without the MSAL wire chatter, which is
/// emitted at Debug and can grow a day-file to hundreds of megabytes.
/// </summary>
public enum LogLevelPreference
{
    /// <summary>Full detail including MSAL internals — for troubleshooting sign-in problems.</summary>
    Debug,

    /// <summary>The app's own activity; MSAL internals are suppressed. Default.</summary>
    Information,

    /// <summary>Problems only.</summary>
    Warning,
}
