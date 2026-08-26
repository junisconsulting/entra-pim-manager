namespace EntraPimManager.Tests.Configuration;

using System.Text.Json;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;

/// <summary>
/// The store does a read-modify-write on the per-user config file. Losing a
/// sibling key here costs the user hand-edited settings with no error to show
/// for it, and a duplicate entry would stop the app at ValidateOnStart.
/// </summary>
public sealed class LocalConfigStoreTests : IDisposable
{
    private const string GlobalAppId = "8f3a1c2e-0000-4000-8000-000000000001";
    private const string ChinaAppId = "8f3a1c2e-0000-4000-8000-000000000002";
    private const string TenantA = "7b1c4d2e-0000-4000-8000-0000000000aa";
    private const string TenantB = "7b1c4d2e-0000-4000-8000-0000000000bb";

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
    public void SaveTenantRegistration_CreatesTheFileAndPreservesSiblings()
    {
        const string existing = """
            {
              "EntraPimManager": { "SomeFutureKey": true },
              "Serilog": { "MinimumLevel": "Debug" }
            }
            """;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, existing);

        LocalConfigStore.SaveTenantRegistration(_filePath, Pinned(TenantA, GlobalAppId, "global", "Contoso"));

        var root = JsonDocument.Parse(File.ReadAllText(_filePath)).RootElement;
        Assert.True(root.GetProperty("EntraPimManager").GetProperty("SomeFutureKey").GetBoolean());
        Assert.Equal("Debug", root.GetProperty("Serilog").GetProperty("MinimumLevel").GetString());

        var entry = Assert.Single(ReadTenantRegistrations());
        Assert.Equal(TenantA, entry.TenantId);
        Assert.Equal(GlobalAppId, entry.ClientId);
        Assert.Equal("Global", entry.Cloud);
        Assert.Equal("Contoso", entry.Label);
    }

    [Fact]
    public void SaveTenantRegistration_CreatesTheDirectoryWhenMissing()
    {
        LocalConfigStore.SaveTenantRegistration(_filePath, Pinned(TenantA, GlobalAppId, "Global", null));

        Assert.Single(ReadTenantRegistrations());
    }

    [Fact]
    public void SaveTenantRegistration_ReplacesTheEntryForTheSameCloudAndTenant()
    {
        // Saving a known tenant is how the user corrects a client id or label —
        // the list must never end up with two entries for one tenant.
        LocalConfigStore.SaveTenantRegistration(_filePath, Pinned(TenantA, GlobalAppId, "Global", "Contoso"));
        LocalConfigStore.SaveTenantRegistration(_filePath, Pinned(TenantB, ChinaAppId, "Global", null));

        LocalConfigStore.SaveTenantRegistration(_filePath, Pinned(TenantA.ToUpperInvariant(), ChinaAppId, "Global", null));

        var entries = ReadTenantRegistrations();
        Assert.Equal(2, entries.Count);
        Assert.Equal(ChinaAppId, entries[0].ClientId);
        Assert.Null(entries[0].Label);
        Assert.Equal(TenantB, entries[1].TenantId);
    }

    [Fact]
    public void RemoveTenantRegistration_RemovesOnlyTheMatchingEntry()
    {
        LocalConfigStore.SaveTenantRegistration(_filePath, Pinned(TenantA, GlobalAppId, "Global", null));
        LocalConfigStore.SaveTenantRegistration(_filePath, Pinned(TenantA, ChinaAppId, "China", null));

        LocalConfigStore.RemoveTenantRegistration(_filePath, EntraCloud.Global, TenantA);
        LocalConfigStore.RemoveTenantRegistration(_filePath, EntraCloud.Global, TenantB);

        var entry = Assert.Single(ReadTenantRegistrations());
        Assert.Equal("China", entry.Cloud);
    }

    [Fact]
    public void SaveTenantRegistration_WritesWhatClientIdForReadsBack()
    {
        // Guards the writer ↔ binder contract: the shape written here must be
        // what the options resolver looks for.
        LocalConfigStore.SaveTenantRegistration(_filePath, Pinned(TenantA, GlobalAppId, "Global", null));

        var options = new EntraPimManagerOptions { TenantAppRegistrations = ReadTenantRegistrations() };

        Assert.Equal(GlobalAppId, options.ClientIdFor(EntraCloud.Global, TenantA));
        Assert.Null(options.ClientIdFor(EntraCloud.Global, TenantB));
    }

    private static TenantAppRegistration Pinned(string tenantId, string clientId, string cloud, string? label)
        => new() { TenantId = tenantId, ClientId = clientId, Cloud = cloud, Label = label };

    private List<TenantAppRegistration> ReadTenantRegistrations()
        => JsonDocument.Parse(File.ReadAllText(_filePath))
            .RootElement
            .GetProperty("EntraPimManager")
            .GetProperty("TenantAppRegistrations")
            .Deserialize<List<TenantAppRegistration>>()!;
}
