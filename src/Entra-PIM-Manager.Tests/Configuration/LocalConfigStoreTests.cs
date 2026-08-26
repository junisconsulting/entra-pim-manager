namespace EntraPimManager.Tests.Configuration;

using System.Text.Json;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;

/// <summary>
/// The store does a read-modify-write on the per-user config file. Losing a
/// sibling key here costs the user their AllowedTenants whitelist or the other
/// cloud's registration, with no error to show for it.
/// </summary>
public sealed class LocalConfigStoreTests : IDisposable
{
    private const string GlobalId = "8f3a1c2e-0000-4000-8000-000000000001";
    private const string ChinaId = "8f3a1c2e-0000-4000-8000-000000000002";
    private const string TenantA = "7b1c4d2e-0000-4000-8000-0000000000aa";
    private const string TenantB = "7b1c4d2e-0000-4000-8000-0000000000bb";
    private const string TenantAppId = "8f3a1c2e-0000-4000-8000-0000000000a1";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "Entra PIM Manager-LocalConfigStore-" + Guid.NewGuid());

    private readonly string _filePath;

    public LocalConfigStoreTests()
        => _filePath = Path.Combine(_directory, "nested", "appsettings.local.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void SaveClientId_CreatesTheFileAndItsDirectory()
    {
        LocalConfigStore.SaveClientId(_filePath, EntraCloud.Global, GlobalId);

        Assert.Equal(GlobalId, ReadRegistration("Global"));
    }

    [Fact]
    public void SaveClientId_KeepsTheOtherCloudsRegistration()
    {
        LocalConfigStore.SaveClientId(_filePath, EntraCloud.Global, GlobalId);
        LocalConfigStore.SaveClientId(_filePath, EntraCloud.China, ChinaId);

        Assert.Equal(GlobalId, ReadRegistration("Global"));
        Assert.Equal(ChinaId, ReadRegistration("China"));
    }

    [Fact]
    public void SaveClientId_OverwritesOnlyTheTargetedCloud()
    {
        LocalConfigStore.SaveClientId(_filePath, EntraCloud.Global, GlobalId);
        LocalConfigStore.SaveClientId(_filePath, EntraCloud.China, ChinaId);

        LocalConfigStore.SaveClientId(_filePath, EntraCloud.China, GlobalId);

        Assert.Equal(GlobalId, ReadRegistration("Global"));
        Assert.Equal(GlobalId, ReadRegistration("China"));
    }

    [Fact]
    public void SaveClientId_PreservesUnrelatedKeys()
    {
        // AllowedTenants is not exposed in the UI — it is hand-edited into this
        // same file, and a Save from Settings must not eat it. The legacy singular
        // ClientId likewise stays put; it is still read as the Global fallback.
        const string existing = """
            {
              "EntraPimManager": {
                "ClientId": "8f3a1c2e-0000-4000-8000-00000000000a",
                "AllowedTenants": [ "8f3a1c2e-0000-4000-8000-00000000000b" ]
              },
              "Serilog": { "MinimumLevel": "Debug" }
            }
            """;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, existing);

        LocalConfigStore.SaveClientId(_filePath, EntraCloud.China, ChinaId);

        var root = JsonDocument.Parse(File.ReadAllText(_filePath)).RootElement;
        var section = root.GetProperty("EntraPimManager");
        Assert.Equal("8f3a1c2e-0000-4000-8000-00000000000a", section.GetProperty("ClientId").GetString());
        Assert.Single(section.GetProperty("AllowedTenants").EnumerateArray());
        Assert.Equal("Debug", root.GetProperty("Serilog").GetProperty("MinimumLevel").GetString());
        Assert.Equal(ChinaId, ReadRegistration("China"));
    }

    [Fact]
    public void SaveClientId_Blank_ClearsTheCloudAndTheLegacyGlobalFallback()
    {
        // Clearing Global must also blank the legacy singular ClientId: it is
        // still read as the Global fallback, so leaving it would bring the old
        // id straight back after the restart.
        const string existing = """
            {
              "EntraPimManager": {
                "ClientId": "8f3a1c2e-0000-4000-8000-00000000000a"
              }
            }
            """;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, existing);
        LocalConfigStore.SaveClientId(_filePath, EntraCloud.Global, GlobalId);
        LocalConfigStore.SaveClientId(_filePath, EntraCloud.China, ChinaId);

        LocalConfigStore.SaveClientId(_filePath, EntraCloud.Global, "  ");

        var section = JsonDocument.Parse(File.ReadAllText(_filePath)).RootElement.GetProperty("EntraPimManager");
        Assert.Equal(string.Empty, ReadRegistration("Global"));
        Assert.Equal(string.Empty, section.GetProperty("ClientId").GetString());
        Assert.Equal(ChinaId, ReadRegistration("China"));

        var options = new EntraPimManagerOptions
        {
            ClientId = section.GetProperty("ClientId").GetString()!,
            AppRegistrations = ReadSection().Deserialize<Dictionary<string, string>>()!,
        };
        Assert.Null(options.ClientIdFor(EntraCloud.Global));
        Assert.Equal(ChinaId, options.ClientIdFor(EntraCloud.China));
    }

    [Fact]
    public void SaveClientId_WritesWhatEntraPimManagerOptionsReadsBack()
    {
        // Guards the contract between the writer and the binder: the nested key
        // shape here must be what ClientIdFor looks for.
        LocalConfigStore.SaveClientId(_filePath, EntraCloud.China, ChinaId);

        var options = new EntraPimManagerOptions
        {
            AppRegistrations = ReadSection().Deserialize<Dictionary<string, string>>()!,
        };

        Assert.Equal(ChinaId, options.ClientIdFor(EntraCloud.China));
    }

    [Fact]
    public void SaveTenantRegistration_AppendsAndPreservesSiblings()
    {
        const string existing = """
            {
              "EntraPimManager": {
                "AppRegistrations": { "Global": "8f3a1c2e-0000-4000-8000-000000000001" },
                "AllowedTenants": [ "8f3a1c2e-0000-4000-8000-00000000000b" ]
              },
              "Serilog": { "MinimumLevel": "Debug" }
            }
            """;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, existing);

        LocalConfigStore.SaveTenantRegistration(_filePath, Pinned(TenantA, TenantAppId, "global", "Contoso"));

        var root = JsonDocument.Parse(File.ReadAllText(_filePath)).RootElement;
        var section = root.GetProperty("EntraPimManager");
        Assert.Equal(GlobalId, ReadRegistration("Global"));
        Assert.Single(section.GetProperty("AllowedTenants").EnumerateArray());
        Assert.Equal("Debug", root.GetProperty("Serilog").GetProperty("MinimumLevel").GetString());

        var entry = Assert.Single(ReadTenantRegistrations());
        Assert.Equal(TenantA, entry.TenantId);
        Assert.Equal(TenantAppId, entry.ClientId);
        Assert.Equal("Global", entry.Cloud);
        Assert.Equal("Contoso", entry.Label);
    }

    [Fact]
    public void SaveTenantRegistration_ReplacesTheEntryForTheSameCloudAndTenant()
    {
        // "Add" with a known tenant is how the user corrects a client id — the
        // list must never end up with two entries for one tenant (the validator
        // would refuse to start the app).
        LocalConfigStore.SaveTenantRegistration(_filePath, Pinned(TenantA, TenantAppId, "Global", "Contoso"));
        LocalConfigStore.SaveTenantRegistration(_filePath, Pinned(TenantB, ChinaId, "Global", null));

        LocalConfigStore.SaveTenantRegistration(_filePath, Pinned(TenantA.ToUpperInvariant(), GlobalId, "Global", null));

        var entries = ReadTenantRegistrations();
        Assert.Equal(2, entries.Count);
        Assert.Equal(GlobalId, entries[0].ClientId);
        Assert.Null(entries[0].Label);
        Assert.Equal(TenantB, entries[1].TenantId);
    }

    [Fact]
    public void RemoveTenantRegistration_RemovesOnlyTheMatchingEntry()
    {
        LocalConfigStore.SaveTenantRegistration(_filePath, Pinned(TenantA, TenantAppId, "Global", null));
        LocalConfigStore.SaveTenantRegistration(_filePath, Pinned(TenantA, ChinaId, "China", null));

        LocalConfigStore.RemoveTenantRegistration(_filePath, EntraCloud.Global, TenantA);
        LocalConfigStore.RemoveTenantRegistration(_filePath, EntraCloud.Global, TenantB);

        var entry = Assert.Single(ReadTenantRegistrations());
        Assert.Equal("China", entry.Cloud);
    }

    [Fact]
    public void SaveTenantRegistration_WritesWhatRegistrationForReadsBack()
    {
        // Guards the writer ↔ binder contract for the list, like the cloud test above.
        LocalConfigStore.SaveClientId(_filePath, EntraCloud.Global, GlobalId);
        LocalConfigStore.SaveTenantRegistration(_filePath, Pinned(TenantA, TenantAppId, "Global", null));

        var options = new EntraPimManagerOptions
        {
            AppRegistrations = ReadSection().Deserialize<Dictionary<string, string>>()!,
            TenantAppRegistrations = ReadTenantRegistrations(),
        };

        Assert.Equal((TenantAppId, TenantA), options.RegistrationFor(EntraCloud.Global, TenantA));
        Assert.Equal((GlobalId, null), options.RegistrationFor(EntraCloud.Global, TenantB));
    }

    private static TenantAppRegistration Pinned(string tenantId, string clientId, string cloud, string? label)
        => new() { TenantId = tenantId, ClientId = clientId, Cloud = cloud, Label = label };

    private JsonElement ReadSection()
        => JsonDocument.Parse(File.ReadAllText(_filePath))
            .RootElement
            .GetProperty("EntraPimManager")
            .GetProperty("AppRegistrations");

    private List<TenantAppRegistration> ReadTenantRegistrations()
        => JsonDocument.Parse(File.ReadAllText(_filePath))
            .RootElement
            .GetProperty("EntraPimManager")
            .GetProperty("TenantAppRegistrations")
            .Deserialize<List<TenantAppRegistration>>()!;

    private string? ReadRegistration(string cloud)
        => ReadSection().GetProperty(cloud).GetString();
}
