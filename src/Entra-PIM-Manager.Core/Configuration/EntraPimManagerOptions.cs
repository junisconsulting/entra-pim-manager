namespace EntraPimManager.Core.Configuration;

using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// The page the junis-hosted admin-consent link returns to, published from <c>site/consent/</c>.
    /// Registered on the junis registration as its only Web redirect URI, so it never changes:
    /// released builds carry it.
    /// </summary>
    public const string JunisConsentPage = "https://junisconsulting.github.io/entra-pim-manager/consent/";

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
    /// Client id of the multi-tenant App Registration junis publishes, in canonical GUID
    /// form, or <c>null</c> when this build ships none — then the app offers no
    /// junis-hosted option at all. Global cloud only.
    /// </summary>
    /// <remarks>
    /// The private setter is the point: the configuration binder skips it, so neither
    /// <c>appsettings.local.json</c> can point "junis-hosted" — and the consent link the app
    /// hands to an admin — at another app. The only source is the shipped
    /// <c>appsettings.json</c>, read by <see cref="ReadShippedJunisHostedClientId"/> and
    /// applied through <see cref="UseJunisHostedClientId"/>.
    /// </remarks>
    public string? JunisHostedClientId { get; private set; }

    /// <summary>
    /// Reads <c>EntraPimManager:JunisHostedClientId</c> from the shipped
    /// <c>appsettings.json</c> at <paramref name="appSettingsPath"/>, or <c>null</c> when the
    /// file or the key is missing or unreadable. Never throws: a broken value must not stop
    /// the app at launch, it only means no junis-hosted option.
    /// </summary>
    public static string? ReadShippedJunisHostedClientId(string appSettingsPath)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(appSettingsPath));
            return root?[SectionName]?["JunisHostedClientId"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sets <see cref="JunisHostedClientId"/>. Anything that is not a GUID counts as none:
    /// a junis-hosted entry would copy it into <c>ClientId</c>, and the validator rejects a
    /// non-GUID there at the next start.
    /// </summary>
    public void UseJunisHostedClientId(string? clientId)
        => JunisHostedClientId = Guid.TryParse(clientId, out var parsed) ? parsed.ToString() : null;

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

    /// <summary>
    /// Whose registration <paramref name="clientId"/> in <paramref name="cloud"/> is. The one
    /// rule behind the hosting chip, the device-code gate, the error wording and the consent
    /// link: junis-hosted only in the Global cloud and only for the shipped junis client id.
    /// </summary>
    public AppRegistrationHosting HostingOf(EntraCloud cloud, string? clientId)
        => cloud == EntraCloud.Global
            && Guid.TryParse(JunisHostedClientId, out var junis)
            && Guid.TryParse(clientId, out var parsed)
            && parsed == junis
                ? AppRegistrationHosting.JunisHosted
                : AppRegistrationHosting.SelfHosted;

    /// <summary>
    /// Whose registration the tenant <paramref name="tenantId"/> in <paramref name="cloud"/>
    /// signs in with, as configured at startup. A tenant without an entry counts as self-hosted.
    /// </summary>
    public AppRegistrationHosting HostingFor(EntraCloud cloud, string? tenantId)
        => HostingOf(cloud, ClientIdFor(cloud, tenantId));

    /// <summary>
    /// The scopes the interactive sign-in asks for. Junis-hosted asks for Microsoft Graph's
    /// <c>.default</c>: on a tenant's first consent Entra then lists every permission the
    /// registration declares, Azure Service Management included, so one admin consent in the
    /// sign-in window covers Azure as well (field test 2026-10-20). The token stays Graph-only.
    /// Self-hosted keeps the explicit list: a customer's registration may declare more than the
    /// app needs, and <c>.default</c> would consent to it and put all of it into every token.
    /// </summary>
    /// <remarks>
    /// Only a first consent prompts. A tenant that has already granted Graph sees no prompt, so
    /// its Azure permission still comes from the consent link.
    /// </remarks>
    public string[] EnrollmentScopes(EntraCloud cloud, string? clientId)
        => HostingOf(cloud, clientId) == AppRegistrationHosting.JunisHosted
            ? ["https://graph.microsoft.com/.default"]
            : Scopes;

    /// <summary>
    /// The client id a new or edited entry gets when the user chose <paramref name="hosting"/>
    /// in <paramref name="cloud"/> and typed <paramref name="typedClientId"/>, or <c>null</c>
    /// when that combination is not valid. Junis-hosted ignores the typed value and needs the
    /// Global cloud and a shipped junis client id; self-hosted needs a GUID, trimmed as typed.
    /// One rule for the add form and the gear editor, so neither can save an entry the next
    /// start rejects.
    /// </summary>
    public string? ResolveClientId(AppRegistrationHosting hosting, EntraCloud cloud, string? typedClientId)
        => hosting == AppRegistrationHosting.JunisHosted
            ? cloud == EntraCloud.Global ? JunisHostedClientId : null
            : Guid.TryParse(typedClientId, out _) ? typedClientId!.Trim() : null;

    /// <summary>
    /// The admin-consent link for the junis-hosted registration in <paramref name="tenantId"/>,
    /// or <c>null</c> outside the Global cloud, for a tenant id that is not a GUID, or when this
    /// build ships no junis client id. Same parameters as the link in
    /// <c>docs/app-registration-setup.md</c>. The redirect URI is junis' consent page
    /// (<see cref="JunisConsentPage"/>), which tells the admin whether the consent went through;
    /// the broker URI left the browser loading forever (field test 2026-10-17).
    /// </summary>
    public string? JunisAdminConsentUrl(EntraCloud cloud, string? tenantId)
    {
        if (cloud != EntraCloud.Global || JunisHostedClientId is not { } clientId || !Guid.TryParse(tenantId, out var tenant))
        {
            return null;
        }

        var redirectUri = Uri.EscapeDataString(JunisConsentPage);
        return $"{EntraCloudInfo.AuthorityBaseUrl(cloud)}/{tenant}/adminconsent?client_id={clientId}&redirect_uri={redirectUri}";
    }
}
