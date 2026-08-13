namespace EntraPimManager.Tests.Configuration;

using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;

/// <summary>
/// Covers the cloud → client id resolution. National clouds are isolated instances,
/// so picking the wrong client id for a cloud is not a cosmetic bug — it sends a
/// Global registration to the 21Vianet authority and fails with AADSTS700016.
/// </summary>
public sealed class EntraPimManagerOptionsTests
{
    private const string GlobalId = "8f3a1c2e-0000-4000-8000-000000000001";
    private const string ChinaId = "8f3a1c2e-0000-4000-8000-000000000002";

    [Fact]
    public void ClientIdFor_ResolvesEachCloudToItsOwnRegistration()
    {
        var options = new EntraPimManagerOptions
        {
            AppRegistrations = { ["Global"] = GlobalId, ["China"] = ChinaId },
        };

        Assert.Equal(GlobalId, options.ClientIdFor(EntraCloud.Global));
        Assert.Equal(ChinaId, options.ClientIdFor(EntraCloud.China));
    }

    [Theory]
    [InlineData("global")]
    [InlineData("GLOBAL")]
    public void ClientIdFor_MatchesCloudKeyCaseInsensitively(string key)
    {
        // appsettings.local.json is hand-edited; a lowercase key must still resolve.
        var options = new EntraPimManagerOptions { AppRegistrations = { [key] = GlobalId } };

        Assert.Equal(GlobalId, options.ClientIdFor(EntraCloud.Global));
    }

    [Fact]
    public void ClientIdFor_FallsBackToLegacyClientIdForGlobalOnly()
    {
        // Config files written before per-cloud registrations existed carry a bare
        // ClientId. It was necessarily a Global registration — never a China one.
        var options = new EntraPimManagerOptions { ClientId = GlobalId };

        Assert.Equal(GlobalId, options.ClientIdFor(EntraCloud.Global));
        Assert.Null(options.ClientIdFor(EntraCloud.China));
    }

    [Fact]
    public void ClientIdFor_PrefersTheMapOverTheLegacyClientId()
    {
        var options = new EntraPimManagerOptions
        {
            ClientId = ChinaId,
            AppRegistrations = { ["Global"] = GlobalId },
        };

        Assert.Equal(GlobalId, options.ClientIdFor(EntraCloud.Global));
    }

    [Fact]
    public void ClientIdFor_TreatsABlankEntryAsUnconfigured()
    {
        // The Settings UI can leave a cloud's key present but empty.
        var options = new EntraPimManagerOptions
        {
            AppRegistrations = { ["Global"] = GlobalId, ["China"] = "   " },
        };

        Assert.Null(options.ClientIdFor(EntraCloud.China));
    }

    [Fact]
    public void ClientIdFor_ReturnsNullWhenNothingIsConfigured()
    {
        var options = new EntraPimManagerOptions();

        Assert.Null(options.ClientIdFor(EntraCloud.Global));
        Assert.Null(options.ClientIdFor(EntraCloud.China));
    }

    [Fact]
    public void ConfiguredClouds_ListsOnlyCloudsWithAGuidClientId()
    {
        // The committed appsettings.json ships "YOUR-CLIENT-ID-HERE" — a configured
        // key that is not a usable registration. It must not reach the cloud picker.
        var options = new EntraPimManagerOptions
        {
            AppRegistrations = { ["Global"] = "YOUR-CLIENT-ID-HERE", ["China"] = ChinaId },
        };

        Assert.Equal([EntraCloud.China], options.ConfiguredClouds());
    }

    [Fact]
    public void ConfiguredClouds_IsEmptyWhenNothingIsConfigured()
        => Assert.Empty(new EntraPimManagerOptions().ConfiguredClouds());
}
