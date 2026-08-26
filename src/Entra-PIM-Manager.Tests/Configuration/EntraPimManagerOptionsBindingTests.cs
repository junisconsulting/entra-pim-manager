namespace EntraPimManager.Tests.Configuration;

using System.Text;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Proves how <c>TenantAppRegistrations</c> survives the layered configuration in
/// <c>App.BuildHost</c> (shipped appsettings.json → per-user file). Arrays are the
/// one place where <c>IConfiguration</c>'s per-key merge is surprising, and the
/// shipped file's shape is decided by these tests.
/// </summary>
public sealed class EntraPimManagerOptionsBindingTests
{
    private const string Shipped = """
        {
          "EntraPimManager": {
            "Scopes": [ "User.Read" ]
          }
        }
        """;

    [Fact]
    public void Bind_ListOnlyInThePerUserFile_YieldsEveryEntryWithDefaults()
    {
        const string perUser = """
            {
              "EntraPimManager": {
                "TenantAppRegistrations": [
                  { "TenantId": "7b1c4d2e-0000-4000-8000-0000000000aa", "ClientId": "8f3a1c2e-0000-4000-8000-0000000000a1", "Label": "Contoso" },
                  { "TenantId": "7b1c4d2e-0000-4000-8000-0000000000bb", "ClientId": "8f3a1c2e-0000-4000-8000-0000000000b1", "Cloud": "China" }
                ]
              }
            }
            """;

        var options = Bind(Shipped, perUser);

        Assert.Equal(["User.Read"], options.Scopes);
        Assert.Equal(2, options.TenantAppRegistrations.Count);
        Assert.Equal("Global", options.TenantAppRegistrations[0].Cloud);
        Assert.Equal("Contoso", options.TenantAppRegistrations[0].Label);
        Assert.Null(options.TenantAppRegistrations[1].Label);
        Assert.Equal("8f3a1c2e-0000-4000-8000-0000000000a1", options.ClientIdFor(EntraCloud.Global, "7b1c4d2e-0000-4000-8000-0000000000aa"));
        Assert.Equal([EntraCloud.Global, EntraCloud.China], options.ConfiguredClouds());
    }

    [Fact]
    public void Bind_ShippedFileAlone_HasNoRegistrations()
        => Assert.Empty(Bind(Shipped).TenantAppRegistrations);

    [Fact]
    public void Bind_ListInTwoLayers_OverlaysEntriesIndexByIndex()
    {
        // This is why the shipped appsettings.json must NOT carry the key, not even
        // as a placeholder: a lower-layer entry bleeds field by field into the
        // user's entry at the same index, and a shorter upper list cannot remove
        // the lower layer's extra entries.
        const string lower = """
            {
              "EntraPimManager": {
                "TenantAppRegistrations": [
                  { "TenantId": "7b1c4d2e-0000-4000-8000-0000000000aa", "ClientId": "8f3a1c2e-0000-4000-8000-0000000000a1", "Label": "Lower" },
                  { "TenantId": "7b1c4d2e-0000-4000-8000-0000000000bb", "ClientId": "8f3a1c2e-0000-4000-8000-0000000000b1" }
                ]
              }
            }
            """;
        const string upper = """
            {
              "EntraPimManager": {
                "TenantAppRegistrations": [
                  { "TenantId": "7b1c4d2e-0000-4000-8000-0000000000cc", "ClientId": "8f3a1c2e-0000-4000-8000-0000000000c1" }
                ]
              }
            }
            """;

        var options = Bind(lower, upper);

        Assert.Equal(2, options.TenantAppRegistrations.Count);
        Assert.Equal("7b1c4d2e-0000-4000-8000-0000000000cc", options.TenantAppRegistrations[0].TenantId);
        Assert.Equal("Lower", options.TenantAppRegistrations[0].Label);
    }

    private static EntraPimManagerOptions Bind(params string[] layers)
    {
        var builder = new ConfigurationBuilder();
        foreach (var layer in layers)
        {
            builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(layer)));
        }

        return builder.Build().GetSection(EntraPimManagerOptions.SectionName).Get<EntraPimManagerOptions>()!;
    }
}
