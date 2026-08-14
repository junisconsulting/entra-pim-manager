namespace EntraPimManager.Tests.Diagnostics;

using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Diagnostics;

/// <summary>
/// Pins the catalog contents. The hosts come from Microsoft's official
/// endpoint lists; a probe against a wrong host would fail everywhere and
/// erode trust in the whole report.
/// </summary>
public sealed class NetworkProbeCatalogTests
{
    [Fact]
    public void GroupsFor_ChinaConfigured_IncludesGlobalLoginHostAndHttpsOnly()
    {
        var groups = NetworkProbeCatalog.GroupsFor([EntraCloud.China]);

        var china = Assert.Single(groups, g => !g.IsOptional);
        Assert.Equal("Entra China (21Vianet)", china.DisplayName);

        var hosts = china.Probes.Select(p => p.Url.Host).ToList();
        Assert.Contains("login.partner.microsoftonline.cn", hosts);

        // 21Vianet row 13: the global login host is required even in the China
        // cloud — the sign-in page pulls from it.
        Assert.Contains("login.microsoftonline.com", hosts);
        Assert.Contains("device.login.partner.microsoftonline.cn", hosts);
        Assert.Contains("aadcdn.msauth.cn", hosts);
        Assert.Contains("aadcdn.msftauth.cn", hosts);
        Assert.Contains("microsoftgraph.chinacloudapi.cn", hosts);
        Assert.All(china.Probes, p => Assert.Equal(Uri.UriSchemeHttps, p.Url.Scheme));
    }

    [Fact]
    public void GroupsFor_GlobalConfigured_IncludesWamCdnAndGraphHosts()
    {
        var groups = NetworkProbeCatalog.GroupsFor([EntraCloud.Global]);

        var global = Assert.Single(groups, g => !g.IsOptional);
        var hosts = global.Probes.Select(p => p.Url.Host).ToList();

        Assert.Contains("login.microsoftonline.com", hosts);
        Assert.Contains("login.microsoft.com", hosts);
        Assert.Contains("login.windows.net", hosts);
        Assert.Contains("device.login.microsoftonline.com", hosts);
        Assert.Contains("aadcdn.msauth.net", hosts);
        Assert.Contains("aadcdn.msftauth.net", hosts);
        Assert.Contains("logincdn.msftauth.net", hosts);
        Assert.Contains("graph.microsoft.com", hosts);
    }

    [Fact]
    public void GroupsFor_Always_AppendsOptionalUpdateFeedGroupLast()
    {
        var groups = NetworkProbeCatalog.GroupsFor([]);

        var update = Assert.Single(groups);
        Assert.True(update.IsOptional);
        Assert.Contains("api.github.com", update.Probes.Select(p => p.Url.Host));
    }

    [Fact]
    public void GroupsFor_BothClouds_PreservesOrder()
    {
        var groups = NetworkProbeCatalog.GroupsFor([EntraCloud.Global, EntraCloud.China]);

        Assert.Equal(3, groups.Count);
        Assert.Equal("Entra Global", groups[0].DisplayName);
        Assert.Equal("Entra China (21Vianet)", groups[1].DisplayName);
        Assert.True(groups[2].IsOptional);
    }

    [Theory]
    [InlineData(EntraCloud.Global, "https://login.microsoftonline.com")]
    [InlineData(EntraCloud.China, "https://login.partner.microsoftonline.cn")]
    public void AuthorityBaseUrl_MatchesCloud(EntraCloud cloud, string expected)
    {
        Assert.Equal(expected, EntraCloudInfo.AuthorityBaseUrl(cloud));
    }
}
