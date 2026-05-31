namespace EntraPimManager.Core.Configuration;

/// <summary>
/// Persisted user preferences for the in-app Settings panel and per-user
/// shell layout. Everything else (auth config, refresh intervals, internal
/// timeouts) lives elsewhere — this record holds only fields the shell
/// reads back at startup.
/// </summary>
/// <param name="Theme">Light / Dark / follow Windows.</param>
/// <param name="DefaultDurationHours">Vorbelegung des Activation-Slider beim Öffnen einer Rolle.</param>
/// <param name="ExpiryWarningEnabled">True to raise a toast before an active assignment expires.</param>
/// <param name="ExpiryWarningMinutes">Wie viele Minuten vor Ablauf der Warn-Toast gefeuert wird.</param>
/// <param name="LastUsedAccountKey">
/// Composite enrollment key (<c>{oid}|{tid}|{cloud}</c>) of the account the
/// user last interacted with. Drives the default expanded eligibility group
/// on startup. <c>null</c> until the first activation or account switch.
/// </param>
/// <param name="ExpandedTenants">
/// Per-tenant expansion state for the ELIGIBILITIES group list, keyed by
/// tenant id. <c>null</c> means "no preferences known yet, use defaults".
/// Stored as a concrete <see cref="Dictionary{TKey,TValue}"/> for direct
/// System.Text.Json round-trip.
/// </param>
/// <param name="SettingsAccountsExpanded">
/// Whether the ACCOUNTS section in the Settings slide-in is expanded or
/// collapsed. Persisted so the user's preference survives an app restart.
/// </param>
public sealed record UserSettings(
    ThemePreference Theme,
    double DefaultDurationHours,
    bool ExpiryWarningEnabled,
    int ExpiryWarningMinutes,
    string? LastUsedAccountKey = null,
    Dictionary<string, bool>? ExpandedTenants = null,
    bool SettingsAccountsExpanded = true)
{
    /// <summary>Defaults applied when no settings file exists or the file is unreadable.</summary>
    public static UserSettings Default { get; } = new(
        Theme: ThemePreference.System,
        DefaultDurationHours: 1.0,
        ExpiryWarningEnabled: true,
        ExpiryWarningMinutes: 5);
}
