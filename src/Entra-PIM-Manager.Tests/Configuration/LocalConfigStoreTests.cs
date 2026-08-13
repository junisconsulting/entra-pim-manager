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

    private JsonElement ReadSection()
        => JsonDocument.Parse(File.ReadAllText(_filePath))
            .RootElement
            .GetProperty("EntraPimManager")
            .GetProperty("AppRegistrations");

    private string? ReadRegistration(string cloud)
        => ReadSection().GetProperty(cloud).GetString();
}
