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
    /// Every App Registration the app may sign in with, each pinned to one tenant.
    /// A multi-tenant registration serving several tenants simply appears once per
    /// tenant with the same client id. This list is also the whitelist: a tenant
    /// without an entry cannot be enrolled. Never shipped in the default
    /// <c>appsettings.json</c>: <c>IConfiguration</c> overlays arrays index by index
    /// across layers, so a shipped placeholder entry would bleed into the user's first entry.
    /// </summary>
    /// <remarks>
    /// Pre-0.7.0 configurations carried one client id per cloud (<c>AppRegistrations:{Cloud}</c>,
    /// earlier a bare <c>ClientId</c>) plus an optional <c>AllowedTenants</c> whitelist.
    /// <see cref="LegacyRegistrationMigration"/> folds those into this list at startup.
    /// </remarks>
    public List<TenantAppRegistration> TenantAppRegistrations { get; set; } = [];

    /// <summary>Delegated Microsoft Graph scopes requested for PIM activation flows.</summary>
    public string[] Scopes { get; set; } = [];

    /// <summary>
    /// Client id to authenticate with for <paramref name="tenantId"/> in <paramref name="cloud"/>,
    /// or <c>null</c> when no registration is pinned to that tenant. This is the whole
    /// login → registration mapping: an enrollment is identified by (oid, tenantId, cloud),
    /// so its registration is derived from configuration on every call and nothing about
    /// it is persisted per account.
    /// </summary>
    public string? ClientIdFor(EntraCloud cloud, string? tenantId)
    {
        if (!Guid.TryParse(tenantId, out var tenant))
        {
            return null;
        }

        return TenantAppRegistrations.FirstOrDefault(r => r.IsFor(cloud, tenant))?.ClientId.Trim();
    }

    /// <summary>
    /// Clouds with at least one registration, in <see cref="EntraCloud"/> declaration
    /// order. Drives the first-run configuration gate and the network diagnostics.
    /// </summary>
    public IReadOnlyList<EntraCloud> ConfiguredClouds()
        => [.. Enum.GetValues<EntraCloud>().Where(c => TenantAppRegistrations.Any(r => r.IsFor(c)))];
}
