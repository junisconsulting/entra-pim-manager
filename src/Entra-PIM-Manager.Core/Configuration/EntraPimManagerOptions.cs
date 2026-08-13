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
    /// Optional whitelist of Entra tenant ids (GUIDs) the user is allowed to enroll.
    /// Empty / <c>null</c> means any tenant is allowed. Used to lock the app down to
    /// a known set of partner tenants when desired.
    /// </summary>
    public string[]? AllowedTenants { get; set; }

    /// <summary>Delegated Microsoft Graph scopes requested for PIM activation flows.</summary>
    public string[] Scopes { get; set; } = [];

    /// <summary>
    /// Client id to authenticate with in <paramref name="cloud"/>, or <c>null</c> when no
    /// app registration is configured for it. Lookup is case-insensitive so a hand-edited
    /// <c>appsettings.local.json</c> with <c>"global"</c> still resolves.
    /// </summary>
    public string? ClientIdFor(EntraCloud cloud)
    {
        var name = cloud.ToString();
        foreach (var (key, value) in AppRegistrations)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return cloud == EntraCloud.Global && !string.IsNullOrWhiteSpace(ClientId)
            ? ClientId.Trim()
            : null;
    }

    /// <summary>
    /// Clouds that have a syntactically usable (GUID) app registration configured, in
    /// <see cref="EntraCloud"/> declaration order. Drives the cloud picker and the
    /// first-run configuration gate.
    /// </summary>
    public IReadOnlyList<EntraCloud> ConfiguredClouds()
        => [.. Enum.GetValues<EntraCloud>().Where(c => Guid.TryParse(ClientIdFor(c), out _))];
}
