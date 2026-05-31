namespace EntraPimManager.Core.Auth;

/// <summary>
/// The sovereign Entra cloud an enrollment lives in. The home account is
/// implicitly <see cref="Global"/> (its cloud is dictated by the Windows
/// account picker); only the "Connect to another tenant…" flow lets the
/// user choose.
/// </summary>
/// <remarks>
/// Each cloud has its own STS authority and its own Microsoft Graph
/// endpoint. The <see cref="MsalAuthService"/> keeps one
/// <c>IPublicClientApplication</c> per cloud, and the Graph factory points
/// the SDK at the matching base URL. See <see cref="EntraCloudInfo"/>.
/// </remarks>
public enum EntraCloud
{
    /// <summary>
    /// Worldwide / commercial Entra. Authority
    /// <c>https://login.microsoftonline.com/</c>, Graph
    /// <c>https://graph.microsoft.com/v1.0</c>. Default for legacy
    /// enrollments persisted before this field existed.
    /// </summary>
    Global = 0,

    /// <summary>
    /// Microsoft Cloud for China, operated by 21Vianet. Authority
    /// <c>https://login.partner.microsoftonline.cn/</c>, Graph
    /// <c>https://microsoftgraph.chinacloudapi.cn/v1.0</c>.
    /// </summary>
    China = 1,
}
