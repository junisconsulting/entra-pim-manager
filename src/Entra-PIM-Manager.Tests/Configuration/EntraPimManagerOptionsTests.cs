namespace EntraPimManager.Tests.Configuration;

using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;

/// <summary>
/// Covers the (cloud, tenant) → client id resolution — the whole login → registration
/// mapping. A wrong pick is not cosmetic: it sends a client id to a directory that
/// has never seen it and fails with AADSTS700016.
/// </summary>
public sealed class EntraPimManagerOptionsTests
{
    private const string GlobalAppId = "8f3a1c2e-0000-4000-8000-000000000001";
    private const string ChinaAppId = "8f3a1c2e-0000-4000-8000-000000000002";
    private const string TenantA = "7b1c4d2e-0000-4000-8000-0000000000aa";
    private const string TenantB = "7b1c4d2e-0000-4000-8000-0000000000bb";

    [Fact]
    public void ClientIdFor_ResolvesTheEntryPinnedToTheTenant()
    {
        // A multi-tenant registration used in two tenants is two entries with the
        // same client id; a customer's own registration is a third with its own.
        var options = new EntraPimManagerOptions
        {
            TenantAppRegistrations =
            {
                Pinned(TenantA, GlobalAppId, "Global"),
                Pinned(TenantB, ChinaAppId, "Global"),
            },
        };

        Assert.Equal(GlobalAppId, options.ClientIdFor(EntraCloud.Global, TenantA));
        Assert.Equal(ChinaAppId, options.ClientIdFor(EntraCloud.Global, TenantB));
    }

    [Theory]
    [InlineData("7B1C4D2E-0000-4000-8000-0000000000AA")]
    [InlineData("{7b1c4d2e-0000-4000-8000-0000000000aa}")]
    public void ClientIdFor_MatchesTheTenantAsAGuidRegardlessOfCaseOrBraces(string input)
    {
        // The tenant id is the matching key; a hand-edited file must not miss on formatting.
        var options = new EntraPimManagerOptions { TenantAppRegistrations = { Pinned(TenantA, GlobalAppId, "global") } };

        Assert.Equal(GlobalAppId, options.ClientIdFor(EntraCloud.Global, input));
    }

    [Fact]
    public void ClientIdFor_IgnoresAnEntryOfAnotherCloud()
    {
        // Tenant GUIDs are unique across clouds, but the entry still names its
        // cloud — a China entry must never be picked for a Global sign-in.
        var options = new EntraPimManagerOptions { TenantAppRegistrations = { Pinned(TenantA, ChinaAppId, "China") } };

        Assert.Null(options.ClientIdFor(EntraCloud.Global, TenantA));
        Assert.Equal(ChinaAppId, options.ClientIdFor(EntraCloud.China, TenantA));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("contoso.com")]
    [InlineData(TenantB)]
    public void ClientIdFor_ReturnsNullForATenantWithoutAnEntry(string? input)
    {
        // The list is the whitelist: blank, a domain, or an unlisted GUID all
        // resolve to nothing — there is no cloud-wide fallback any more.
        var options = new EntraPimManagerOptions { TenantAppRegistrations = { Pinned(TenantA, GlobalAppId, "Global") } };

        Assert.Null(options.ClientIdFor(EntraCloud.Global, input));
    }

    [Fact]
    public void ConfiguredClouds_ListsCloudsWithAnEntryInDeclarationOrder()
    {
        var options = new EntraPimManagerOptions
        {
            TenantAppRegistrations =
            {
                Pinned(TenantA, ChinaAppId, "China"),
                Pinned(TenantB, GlobalAppId, "Global"),
            },
        };

        Assert.Equal([EntraCloud.Global, EntraCloud.China], options.ConfiguredClouds());
    }

    [Fact]
    public void ConfiguredClouds_IsEmptyWhenNothingIsConfigured()
        => Assert.Empty(new EntraPimManagerOptions().ConfiguredClouds());

    private static TenantAppRegistration Pinned(string tenantId, string clientId, string cloud)
        => new() { TenantId = tenantId, ClientId = clientId, Cloud = cloud };
}
