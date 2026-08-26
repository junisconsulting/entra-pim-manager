namespace EntraPimManager.Core.Configuration;

using EntraPimManager.Core.Auth;

/// <summary>
/// An App Registration pinned to exactly one tenant — the single-tenant counterpart
/// of the cloud-wide, multi-tenant registration in
/// <see cref="EntraPimManagerOptions.AppRegistrations"/>. Bound from one element of
/// <c>EntraPimManager:TenantAppRegistrations</c>.
/// </summary>
/// <remarks>
/// Exists for customers whose security policy forbids consenting to a foreign
/// multi-tenant app: they register their own single-tenant app and hand over its
/// client id. A tenant registration takes precedence over the cloud registration
/// for its tenant — see <see cref="EntraPimManagerOptions.RegistrationFor"/>.
/// <para/>
/// <see cref="Cloud"/> is a string rather than <see cref="EntraCloud"/> so that a
/// hand-edited typo is reported by <see cref="EntraPimManagerOptionsValidator"/>
/// with a named message instead of failing inside the configuration binder.
/// </remarks>
public sealed class TenantAppRegistration
{
    /// <summary>Tenant (directory) id this registration belongs to. Always a GUID.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Application (client) id of the single-tenant registration.</summary>
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
