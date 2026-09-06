namespace EntraPimManager.Core.Configuration;

using EntraPimManager.Core.Auth;

/// <summary>
/// One tenant as the Settings tree shows it: the slot an App Registration is pinned
/// to, the tenant an enrolled account belongs to, or — usually — both.
/// </summary>
/// <remarks>
/// The identity is (cloud, tenant), the same slot identity the registration store and
/// <see cref="EntraPimManagerOptions.ClientIdFor"/> already use. Reusing it is the
/// point: a second, subtly different notion of "the same tenant" is how the two lists
/// this type replaces drifted apart in the first place.
/// </remarks>
/// <param name="Cloud">Sovereign cloud the tenant lives in.</param>
/// <param name="TenantId">Tenant id as configured or as MSAL reports it — unnormalised, for display.</param>
public sealed record TenantSlot(EntraCloud Cloud, string TenantId)
{
    /// <summary>
    /// Identity of the slot. Built from the normalised tenant id, so the same tenant
    /// written three ways in three places is one slot.
    /// </summary>
    public string Key => $"{Cloud}|{NormaliseTenantId(TenantId)}";

    /// <summary>
    /// Canonical form of a tenant id for comparison.
    /// </summary>
    /// <remarks>
    /// <see cref="Guid.TryParse(string, out Guid)"/> rather than a lower-casing of the
    /// raw string: <c>appsettings.local.json</c> is hand-editable and <c>{guid}</c> and
    /// <c>(guid)</c> parse, so lower-casing alone would still yield a second slot for a
    /// tenant that is already configured. A value that is no GUID at all is kept (trimmed
    /// and folded) instead of rejected — the file reaches this code before any validator
    /// could stop it, and one odd-looking card beats a crash.
    /// </remarks>
    /// <param name="tenantId">The raw tenant id.</param>
    /// <returns>The canonical form, or an empty string when there is nothing to canonicalise.</returns>
    public static string NormaliseTenantId(string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return string.Empty;
        }

        return Guid.TryParse(tenantId, out var parsed)
            ? parsed.ToString("D")
            : tenantId.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// The tenants to render, in display order: those with at least one enrolled account
    /// first — in the order the accounts appear — then the configured tenants nobody has
    /// signed into yet, in configuration order.
    /// </summary>
    /// <remarks>
    /// Account order first, because the popup orders its eligibility groups by the same
    /// account collection; sorting alphabetically here would make Settings and the popup
    /// permanently disagree. Tenants without an account land at the bottom, which is where
    /// their "add an account" call to action belongs.
    /// <para/>
    /// Duplicates collapse rather than throw. A hand-edited config file can name one slot
    /// twice, and this list feeds a rebuild that runs on every account change.
    /// </remarks>
    /// <param name="accountSlots">Slots of the enrolled accounts, in account order.</param>
    /// <param name="registrationSlots">Slots of the configured registrations, in configuration order.</param>
    /// <returns>The merged, deduplicated slot list.</returns>
    public static IReadOnlyList<TenantSlot> Merge(
        IEnumerable<TenantSlot> accountSlots,
        IEnumerable<TenantSlot> registrationSlots)
    {
        ArgumentNullException.ThrowIfNull(accountSlots);
        ArgumentNullException.ThrowIfNull(registrationSlots);

        var merged = new List<TenantSlot>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var slot in accountSlots.Concat(registrationSlots))
        {
            if (seen.Add(slot.Key))
            {
                merged.Add(slot);
            }
        }

        return merged;
    }
}
