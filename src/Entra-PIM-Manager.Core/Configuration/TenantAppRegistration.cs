namespace EntraPimManager.Core.Configuration;

using EntraPimManager.Core.Auth;

/// <summary>
/// One App Registration the app may sign in with, pinned to one tenant. Bound from
/// one element of <c>EntraPimManager:TenantAppRegistrations</c>.
/// </summary>
/// <remarks>
/// The registration in Entra may be multi-tenant (consented in several tenants) or
/// single-tenant (a customer's own app) — the app does not care: it always sends the
/// request to the entry's tenant, which is what a single-tenant app requires and a
/// multi-tenant app accepts. One multi-tenant app used in three tenants is three
/// entries with the same client id.
/// <para/>
/// <see cref="Cloud"/> is a string rather than <see cref="EntraCloud"/> so that a
/// hand-edited typo is reported by <see cref="EntraPimManagerOptionsValidator"/>
/// with a named message instead of failing inside the configuration binder.
/// </remarks>
public sealed class TenantAppRegistration
{
    /// <summary>Tenant (directory) id this registration signs in to. Always a GUID.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Application (client) id of the registration.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Sovereign cloud the tenant lives in, as an <see cref="EntraCloud"/> name.</summary>
    public string Cloud { get; set; } = nameof(EntraCloud.Global);

    /// <summary>Optional display name for the sign-in picker, e.g. the customer's name.</summary>
    public string? Label { get; set; }

    /// <summary>True when <see cref="Cloud"/> names <paramref name="cloud"/> (case-insensitive).</summary>
    public bool IsFor(EntraCloud cloud)
        => Enum.TryParse<EntraCloud>(Cloud, ignoreCase: true, out var parsed) && parsed == cloud;

    /// <summary>True when this registration is pinned to <paramref name="tenantId"/> in <paramref name="cloud"/>.</summary>
    public bool IsFor(EntraCloud cloud, Guid tenantId)
        => IsFor(cloud) && Guid.TryParse(TenantId, out var parsed) && parsed == tenantId;
}
