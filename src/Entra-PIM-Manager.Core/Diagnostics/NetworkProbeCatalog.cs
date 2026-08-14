namespace EntraPimManager.Core.Diagnostics;

using EntraPimManager.Core.Auth;

/// <summary>
/// The endpoints a network check probes, per cloud. Hosts come from
/// Microsoft's official endpoint lists (<em>URLs and IP address ranges</em>,
/// worldwide and 21Vianet editions, rows 13 and 17 for the latter); wildcard
/// rows (<c>*.msauth.cn</c> etc.) are probed via the concrete
/// <c>aadcdn.</c> hosts the sign-in page actually loads from.
/// </summary>
/// <remarks>
/// <para>
/// The sign-in CDN hosts matter most for support: the WAM sign-in window
/// renders the login page plus its CDN assets, so a blocked CDN shows up as a
/// blank window while device-code login (token endpoint only) still works.
/// </para>
/// <para>
/// Deliberately not probed: <c>*.auth.microsoft.com</c> / <c>*.auth.microsoft.cn</c>
/// (required wildcards, but no documented concrete host — the .cn candidates do
/// not even resolve), <c>login.live.com</c> (DefaultOptional; our broker options
/// disable the MSA surfaces, and enterprises legitimately block MSA — a red row
/// here would discredit the report), <c>autologon.microsoftazuread-sso.com</c>
/// (Seamless SSO only) and <c>enterpriseregistration.windows.net</c> (OS device
/// registration, not app sign-in).
/// </para>
/// </remarks>
public static class NetworkProbeCatalog
{
    private const string CdnPurpose = "Sign-in page assets (CDN)";
    private const string UpdatePurpose = "Update feed (GitHub)";

    // Concrete hosts under the wildcard CDN rows of the official endpoint lists.
    // logincdn has no .cn counterpart (verified: does not resolve).
    private static readonly string[] GlobalSignInCdnHosts =
    [
        "aadcdn.msftauth.net",
        "aadcdn.msauth.net",
        "aadcdn.msftauthimages.net",
        "aadcdn.msauthimages.net",
        "logincdn.msftauth.net",
    ];

    private static readonly string[] ChinaSignInCdnHosts =
    [
        "aadcdn.msftauth.cn",
        "aadcdn.msauth.cn",
        "aadcdn.msftauthimages.cn",
        "aadcdn.msauthimages.cn",
    ];

    /// <summary>
    /// Builds the probe groups for <paramref name="configuredClouds"/> (one
    /// group per cloud, in the given order) plus the optional update-feed group.
    /// </summary>
    public static IReadOnlyList<NetworkProbeGroup> GroupsFor(IReadOnlyList<EntraCloud> configuredClouds)
    {
        ArgumentNullException.ThrowIfNull(configuredClouds);

        var groups = new List<NetworkProbeGroup>(configuredClouds.Count + 1);
        foreach (var cloud in configuredClouds)
        {
            groups.Add(GroupFor(cloud));
        }

        groups.Add(UpdateFeedGroup());
        return groups;
    }

    private static NetworkProbeGroup GroupFor(EntraCloud cloud)
    {
        var probes = new List<NetworkProbe>
        {
            Probe(OpenIdConfigurationUrl(EntraCloudInfo.AuthorityBaseUrl(cloud)), "Sign-in (token endpoint)"),
        };

        if (cloud == EntraCloud.China)
        {
            // 21Vianet endpoint list row 13 marks the *global* login host as
            // required even for the China cloud — the China sign-in page pulls
            // from it, making it a prime suspect for a blank WAM window.
            probes.Add(Probe(
                OpenIdConfigurationUrl(EntraCloudInfo.AuthorityBaseUrl(EntraCloud.Global)),
                "Global sign-in (required by the China sign-in page)"));

            // Documented in the Azure China hybrid-join docs, which explicitly
            // require it to be excluded from TLS break-and-inspect — a TLS
            // failure on this row is directly actionable.
            probes.Add(Probe(
                "https://device.login.partner.microsoftonline.cn/",
                "Sign-in (device auth / PRT)"));
        }
        else
        {
            probes.Add(Probe("https://login.microsoft.com/", "Sign-in (WAM)"));
            probes.Add(Probe(
                OpenIdConfigurationUrl("https://login.windows.net"),
                "Sign-in (legacy STS / instance discovery)"));

            // Worldwide endpoint list row 56 (AllowRequired) — device auth /
            // primary refresh token traffic behind the WAM window.
            probes.Add(Probe(
                "https://device.login.microsoftonline.com/",
                "Sign-in (device auth / PRT)"));
        }

        var cdnHosts = cloud == EntraCloud.China ? ChinaSignInCdnHosts : GlobalSignInCdnHosts;
        foreach (var host in cdnHosts)
        {
            probes.Add(Probe($"https://{host}/", CdnPurpose));
        }

        // Probe the Graph service root, not /v1.0 — an anonymous 401/403 from
        // the host already proves reachability, which is all we need.
        var graphRoot = new Uri(EntraCloudInfo.GraphBaseUrl(cloud)).GetLeftPart(UriPartial.Authority) + "/";
        probes.Add(Probe(graphRoot, "Microsoft Graph"));

        return new NetworkProbeGroup(EntraCloudInfo.DisplayName(cloud), IsOptional: false, probes);
    }

    private static NetworkProbeGroup UpdateFeedGroup()
    {
        var probes = new List<NetworkProbe>
        {
            Probe("https://api.github.com/", UpdatePurpose),
            Probe("https://github.com/", UpdatePurpose),
            Probe("https://objects.githubusercontent.com/", UpdatePurpose),
        };

        return new NetworkProbeGroup("Update feed", IsOptional: true, probes);
    }

    private static string OpenIdConfigurationUrl(string authorityBaseUrl)
        => $"{authorityBaseUrl}/common/v2.0/.well-known/openid-configuration";

    private static NetworkProbe Probe(string url, string purpose) => new(new Uri(url), purpose);
}
