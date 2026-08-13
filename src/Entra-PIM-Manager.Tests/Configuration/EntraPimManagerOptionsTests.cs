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
    public void ClientIdFor_ShippedPlaceholderDoesNotShadowALegacyClientId()
    {
        // The real 0.4.1 -> 0.4.2 in-place upgrade. Velopack replaces the install
        // directory's appsettings.json — which now carries AppRegistrations:Global
        // = the placeholder — while the per-user file still holds only the legacy
        // ClientId. IConfiguration merges the two layers PER KEY, so both land in
        // this object at once and the placeholder must lose.
        //
        // Regression: while the placeholder counted as "configured", ConfiguredClouds
        // came back empty, ShellViewModel.NeedsConfiguration flipped to true, and
        // InitializeAsync returned before loading accounts.json — so the upgrade
        // looked like it had wiped both the App Registration and every account.
        var options = new EntraPimManagerOptions
        {
            ClientId = GlobalId,
            AppRegistrations = { ["Global"] = "YOUR-CLIENT-ID-HERE", ["China"] = string.Empty },
        };

        Assert.Equal(GlobalId, options.ClientIdFor(EntraCloud.Global));
        Assert.Equal([EntraCloud.Global], options.ConfiguredClouds());
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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("YOUR-CLIENT-ID-HERE")]
    [InlineData("not-a-guid")]
    public void ClientIdFor_TreatsAnUnusableEntryAsUnconfigured(string value)
    {
        // A cloud's key can be present but empty (Settings leaves an unused cloud
        // blank) or hold a placeholder (the shipped appsettings.json). Entra client
        // ids are always GUIDs, so anything else means "not configured".
        var options = new EntraPimManagerOptions
        {
            AppRegistrations = { ["Global"] = GlobalId, ["China"] = value },
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
