namespace EntraPimManager.Core.Configuration;

using EntraPimManager.Core.Auth;

/// <summary>
/// Strongly-typed application configuration bound from the <c>EntraPimManager</c>
/// section of <c>appsettings.json</c> / <c>appsettings.local.json</c>.
/// </summary>
public sealed class EntraPimManagerOptions
{
    /// <summary>Configuration section name this options object is bound from.</summary>
    public const string SectionName = "EntraPimManager";

    /// <summary>
    /// Legacy single-registration setting, equivalent to <c>AppRegistrations:Global</c>.
    /// Still read so configuration files written before per-cloud registrations existed
    /// keep working; <see cref="AppRegistrations"/> wins when both are present.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Entra application (client) ID per sovereign cloud, keyed by <see cref="EntraCloud"/>
    /// name (e.g. <c>"Global"</c>, <c>"China"</c>). A blank or non-GUID value means that
    /// cloud is not configured — see <see cref="ConfiguredClouds"/>.
    /// </summary>
    /// <remarks>
    /// National clouds are physically isolated instances — an app registration exists in
    /// exactly one of them, so a Global client id is unknown to <c>login.partner.microsoftonline.cn</c>
    /// and vice versa. Within one cloud the registration is multi-tenant
    /// (<c>AzureAdMultipleOrgs</c>) and covers every tenant the user signs into there.
    /// See <c>docs/app-registration-setup.md</c>.
    /// </remarks>
    public Dictionary<string, string> AppRegistrations { get; set; } = [];

    /// <summary>
    /// App Registrations pinned to a single tenant, any number per cloud. Each one
    /// overrides the cloud-wide registration in <see cref="AppRegistrations"/> for its
    /// tenant — see <see cref="RegistrationFor"/>. Never shipped in the default
    /// <c>appsettings.json</c>: <c>IConfiguration</c> overlays arrays index by index across
    /// layers, so a shipped placeholder entry would bleed into the user's first entry.
    /// </summary>
    public List<TenantAppRegistration> TenantAppRegistrations { get; set; } = [];

    /// <summary>
    /// Optional whitelist of Entra tenant ids (GUIDs) the user is allowed to enroll.
    /// Empty / <c>null</c> means any tenant is allowed. Used to lock the app down to
    /// a known set of partner tenants when desired.
    /// </summary>
    public string[]? AllowedTenants { get; set; }

    /// <summary>Delegated Microsoft Graph scopes requested for PIM activation flows.</summary>
    public string[] Scopes { get; set; } = [];

    /// <summary>
    /// Client id to authenticate with in <paramref name="cloud"/>, or <c>null</c> when no
    /// usable app registration is configured for it. Lookup is case-insensitive so a
    /// hand-edited <c>appsettings.local.json</c> with <c>"global"</c> still resolves.
    /// </summary>
    /// <remarks>
    /// Only a GUID counts as configured. Entra client ids are always GUIDs, and treating
    /// anything else as unset is what keeps the shipped <c>"YOUR-CLIENT-ID-HERE"</c>
    /// placeholder from shadowing a real value: <c>IConfiguration</c> merges the shipped
    /// and per-user files per key, so after an upgrade the placeholder in
    /// <c>AppRegistrations:Global</c> and a pre-0.4.2 <see cref="ClientId"/> are both
    /// present at once.
    /// </remarks>
    public string? ClientIdFor(EntraCloud cloud)
    {
        var name = cloud.ToString();
        foreach (var (key, value) in AppRegistrations)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase) && IsUsable(value))
            {
                return value.Trim();
            }
        }

        return cloud == EntraCloud.Global && IsUsable(ClientId) ? ClientId.Trim() : null;
    }

    /// <summary>
    /// The App Registration a sign-in or token request for <paramref name="tenantIdOrDomain"/>
    /// in <paramref name="cloud"/> must use: a usable tenant registration pinned to that
    /// tenant wins, otherwise the cloud-wide registration, otherwise <c>null</c>. A non-null
    /// <c>TenantId</c> in the result means "single-tenant — build the authority for that tenant".
    /// </summary>
    /// <remarks>
    /// This is the whole login → registration mapping. An enrollment is identified by
    /// (oid, tenantId, cloud), so the registration is derived from configuration on
    /// every call and nothing about it is persisted per account.
    /// <para/>
    /// ponytail: tenant registrations match by GUID only. A domain name typed into the
    /// free-text tenant field falls through to the cloud registration; the sign-in picker
    /// pre-fills the GUID for pinned tenants, and MsalAuthService rejects a sign-in that
    /// lands in a pinned tenant through the wrong registration. Resolving domains via
    /// Graph would be the upgrade if hand-typed domains ever matter.
    /// </remarks>
    public (string ClientId, string? TenantId)? RegistrationFor(EntraCloud cloud, string? tenantIdOrDomain)
    {
        if (Guid.TryParse(tenantIdOrDomain, out var tenantId))
        {
            var pinned = TenantAppRegistrations.FirstOrDefault(t => t.IsFor(cloud, tenantId) && IsUsable(t.ClientId));
            if (pinned is not null)
            {
                return (pinned.ClientId.Trim(), tenantId.ToString());
            }
        }

        return ClientIdFor(cloud) is { } clientId ? (clientId, null) : null;
    }

    /// <summary>
    /// Clouds that have at least one usable app registration — cloud-wide or tenant-pinned —
    /// in <see cref="EntraCloud"/> declaration order. Drives the first-run configuration
    /// gate and the network diagnostics.
    /// </summary>
    public IReadOnlyList<EntraCloud> ConfiguredClouds()
        => [.. Enum.GetValues<EntraCloud>().Where(c =>
            ClientIdFor(c) is not null || TenantAppRegistrations.Any(t => t.IsFor(c) && IsUsable(t.ClientId)))];

    private static bool IsUsable(string? clientId) => Guid.TryParse(clientId, out _);
}
