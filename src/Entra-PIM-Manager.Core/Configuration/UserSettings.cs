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
/// Whether the TENANTS section in the Settings slide-in is expanded or
/// collapsed. Persisted so the user's preference survives an app restart.
/// </param>
/// <param name="AutomaticUpdatesEnabled">
/// True to periodically check GitHub Releases for a newer build and offer to
/// install it. Defaults to true. Old settings files written before this field
/// existed deserialize to the constructor default (on), which is the intended
/// behaviour.
/// </param>
/// <param name="VerifiedClientIds">
/// The App Registration client ids a sign-in has actually succeeded with. An id is
/// appended when an account is enrolled; Settings shows a registration as verified
/// only when its configured client id is in this set.
/// <para/>
/// A well-formed GUID proves nothing on its own — the common setup mistakes
/// (pasting the Object or Directory id, "Allow public client flows" off, an
/// unregistered broker redirect URI, missing admin consent) all pass a format
/// check and only surface at sign-in. Recording the ids that actually worked is
/// what keeps the green check honest, including after a client id is changed.
/// <para/>
/// A set rather than a single value because each sovereign cloud has its own
/// registration, and each must prove itself separately. <c>null</c> until the
/// first successful enrollment.
/// </param>
/// <param name="LogLevel">
/// Minimum level for the rolling log files. Applied live via the Serilog
/// level switch — no restart needed. Old settings files without the field
/// deserialize to the constructor default (Information), which is the
/// intended behaviour.
/// </param>
/// <param name="AccountAliases">
/// Short self-chosen names for enrolled accounts ("EADM", "Admin"), keyed by the
/// same composite enrollment key as <paramref name="LastUsedAccountKey"/>
/// (<c>{oid}|{tid}|{cloud}</c>) — the same identity in two tenants is two
/// enrollments and gets two aliases.
/// <para/>
/// Display only: an alias never routes a call, never keys a cache, and is never
/// logged (it is user-typed text that may carry a UPN or a customer name). The
/// popup falls back to the UPN when no alias is set. <c>null</c> until the user
/// renames the first account.
/// </param>
/// <param name="PinnedEligibilities">
/// Eligibilities the user pinned to the top of the list, as
/// <c>{oid}|{tid}|{cloud}|{kind}|{resourceId}|{scopeId}</c> keys. Deliberately not
/// capped — a user who pins twenty has decided that.
/// </param>
/// <param name="RecentEligibilities">
/// The most recently activated eligibilities, same key shape, newest first and
/// capped to a handful. Written on a successful activation; a key that no longer
/// resolves to a live eligibility is skipped when the list is rendered, never
/// offered as a row that would fail on click.
/// </param>
/// <param name="LastSeenVersion">
/// The app version whose release notes the user has already been shown, e.g.
/// <c>"0.9.0"</c>. A different running version opens the What's-new window once;
/// a fresh install records the version without showing anything, because there is
/// nothing "new" about a first launch. <c>null</c> on installs that predate the
/// window — those see it once, which is the intent.
/// </param>
/// <param name="TicketSystems">
/// The ticketing system a tenant uses ("ServiceNow"), keyed by tenant id — one per
/// tenant, prefilled into the activation form when the role's policy asks for a
/// ticket. Editable there, so a one-off exception costs nothing.
/// <para/>
/// Lives here rather than on <see cref="TenantAppRegistration"/> for two reasons: it
/// is workflow, not auth configuration, and a registration change only takes effect
/// after a restart while these settings apply at once.
/// </param>
public sealed record UserSettings(
    ThemePreference Theme,
    double DefaultDurationHours,
    bool ExpiryWarningEnabled,
    int ExpiryWarningMinutes,
    string? LastUsedAccountKey = null,
    Dictionary<string, bool>? ExpandedTenants = null,
    bool SettingsAccountsExpanded = true,
    bool AutomaticUpdatesEnabled = true,

    // ponytail: the pre-0.4.2 `VerifiedClientId` string is deliberately not migrated.
    // The badge falls back to "configured, not verified" once and heals itself on the
    // next sign-in; custom deserialization for a display flag is not worth carrying.
    string[]? VerifiedClientIds = null,
    LogLevelPreference LogLevel = LogLevelPreference.Information,
    Dictionary<string, string>? AccountAliases = null,
    Dictionary<string, string>? TicketSystems = null,
    List<string>? PinnedEligibilities = null,
    List<string>? RecentEligibilities = null,
    string? LastSeenVersion = null)
{
    /// <summary>Defaults applied when no settings file exists or the file is unreadable.</summary>
    public static UserSettings Default { get; } = new(
        Theme: ThemePreference.System,
        DefaultDurationHours: 1.0,
        ExpiryWarningEnabled: true,
        ExpiryWarningMinutes: 5,
        AutomaticUpdatesEnabled: true);

    /// <summary>
    /// The ticketing system configured for <paramref name="tenantId"/>, or <c>null</c>
    /// when the tenant has none. One place for the rule so the Settings form and the
    /// activation form cannot drift apart.
    /// </summary>
    public string? TicketSystemFor(string? tenantId)
    {
        if (tenantId is null || TicketSystems is not { } systems)
        {
            return null;
        }

        // Matched case-insensitively on purpose. The key is written as a lower-case
        // GUID by the Settings form, read back with whatever casing MSAL reports on
        // the account, and may be hand-edited in settings.json — and deserialization
        // drops the comparer the write path chose. A casing mismatch would silently
        // mean "no prefill", which looks like the setting was never saved.
        return systems
            .FirstOrDefault(entry => string.Equals(entry.Key, tenantId, StringComparison.OrdinalIgnoreCase))
            .Value;
    }
}
