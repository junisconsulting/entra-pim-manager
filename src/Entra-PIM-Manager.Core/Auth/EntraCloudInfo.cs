namespace EntraPimManager.Core.Auth;

using Microsoft.Identity.Client;

/// <summary>
/// Static helpers translating an <see cref="EntraCloud"/> into the concrete
/// MSAL + Graph configuration the surrounding layers need. Keeping these in
/// one place avoids each consumer having to remember which cloud constant
/// maps to which URL.
/// </summary>
public static class EntraCloudInfo
{
    /// <summary>Microsoft Graph v1.0 base URL for <paramref name="cloud"/>.</summary>
    public static string GraphBaseUrl(EntraCloud cloud) => cloud switch
    {
        EntraCloud.China => "https://microsoftgraph.chinacloudapi.cn/v1.0",
        _ => "https://graph.microsoft.com/v1.0",
    };

    /// <summary>
    /// STS authority base URL for <paramref name="cloud"/>, no trailing slash.
    /// MSAL derives its authority from <see cref="MsalCloudInstance"/>; this
    /// literal exists for the network diagnostics, which must name the host
    /// explicitly. Keep the two switches in sync.
    /// </summary>
    public static string AuthorityBaseUrl(EntraCloud cloud) => cloud switch
    {
        EntraCloud.China => "https://login.partner.microsoftonline.cn",
        _ => "https://login.microsoftonline.com",
    };

    /// <summary>MSAL cloud instance for <paramref name="cloud"/>.</summary>
    public static AzureCloudInstance MsalCloudInstance(EntraCloud cloud) => cloud switch
    {
        EntraCloud.China => AzureCloudInstance.AzureChina,
        _ => AzureCloudInstance.AzurePublic,
    };

    /// <summary>Short display label for the UI / logs.</summary>
    public static string DisplayName(EntraCloud cloud) => cloud switch
    {
        EntraCloud.China => "Entra China (21Vianet)",
        _ => "Entra Global",
    };
}
