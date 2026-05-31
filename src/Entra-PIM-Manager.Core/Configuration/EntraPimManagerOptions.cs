namespace EntraPimManager.Core.Configuration;

/// <summary>
/// Strongly-typed application configuration bound from the <c>EntraPimManager</c>
/// section of <c>appsettings.json</c> / <c>appsettings.local.json</c>.
/// </summary>
public sealed class EntraPimManagerOptions
{
    /// <summary>Configuration section name this options object is bound from.</summary>
    public const string SectionName = "EntraPimManager";

    /// <summary>
    /// Entra application (client) ID of the Entra PIM Manager app registration. Must be a GUID.
    /// The app registration is multi-tenant (<c>AzureAdMultipleOrgs</c>) — one ClientId
    /// serves every tenant the user signs into.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Optional whitelist of Entra tenant ids (GUIDs) the user is allowed to enroll.
    /// Empty / <c>null</c> means any tenant is allowed. Used to lock the app down to
    /// a known set of partner tenants when desired.
    /// </summary>
    public string[]? AllowedTenants { get; set; }

    /// <summary>Delegated Microsoft Graph scopes requested for PIM activation flows.</summary>
    public string[] Scopes { get; set; } = [];
}
