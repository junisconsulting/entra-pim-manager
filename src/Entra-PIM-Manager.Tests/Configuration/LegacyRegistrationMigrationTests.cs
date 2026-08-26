namespace EntraPimManager.Tests.Configuration;

using System.Text.Json;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;

/// <summary>
/// The 0.6.x → 0.7.0 upgrade: per-cloud client ids become per-tenant entries and the
/// per-cloud cache files get their per-client-id names. Getting this wrong means
/// every existing install greets its user with an empty registration list and a
/// forced re-sign-in.
/// </summary>
public sealed class LegacyRegistrationMigrationTests : IDisposable
{
    private const string GlobalAppId = "8f3a1c2e-0000-4000-8000-000000000001";
    private const string ChinaAppId = "8f3a1c2e-0000-4000-8000-000000000002";
    private const string TenantA = "7b1c4d2e-0000-4000-8000-0000000000aa";
    private const string TenantB = "7b1c4d2e-0000-4000-8000-0000000000bb";
    private const string TenantC = "7b1c4d2e-0000-4000-8000-0000000000cc";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "Entra PIM Manager-Migration-" + Guid.NewGuid());

    private readonly string _configPath;
    private readonly string _accountsPath;

    public LegacyRegistrationMigrationTests()
    {
        Directory.CreateDirectory(_directory);
        _configPath = Path.Combine(_directory, "appsettings.local.json");
        _accountsPath = Path.Combine(_directory, "accounts.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Run_PerCloudClientIds_BecomeOneEntryPerEnrolledTenant()
    {
        WriteConfig($$"""
            {
              "EntraPimManager": {
                "AppRegistrations": { "Global": "{{GlobalAppId}}", "China": "{{ChinaAppId}}" }
              },
              "Serilog": { "MinimumLevel": "Debug" }
            }
            """);
        WriteAccounts(
            Account("oid-1", TenantA, EntraCloud.Global),
            Account("oid-2", TenantA, EntraCloud.Global),
            Account("oid-1", TenantB, EntraCloud.Global),
            Account("oid-3", TenantC, EntraCloud.China));

        var result = LegacyRegistrationMigration.Run(_configPath, _accountsPath, _directory);

        Assert.Equal(3, result.Migrated.Count);
        Assert.Empty(result.Dropped);

        var entries = ReadEntries();
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.TenantId == TenantA && e.ClientId == GlobalAppId && e.Cloud == "Global");
        Assert.Contains(entries, e => e.TenantId == TenantB && e.ClientId == GlobalAppId && e.Cloud == "Global");
        Assert.Contains(entries, e => e.TenantId == TenantC && e.ClientId == ChinaAppId && e.Cloud == "China");

        var root = JsonDocument.Parse(File.ReadAllText(_configPath)).RootElement;
        Assert.False(root.GetProperty("EntraPimManager").TryGetProperty("AppRegistrations", out _));
        Assert.Equal("Debug", root.GetProperty("Serilog").GetProperty("MinimumLevel").GetString());
    }

    [Fact]
    public void Run_RenamesTheLegacyCacheFilesWithoutOverwriting()
    {
        WriteConfig($$"""{ "EntraPimManager": { "AppRegistrations": { "Global": "{{GlobalAppId}}", "China": "{{ChinaAppId}}" } } }""");
        WriteAccounts(Account("oid-1", TenantA, EntraCloud.Global), Account("oid-2", TenantC, EntraCloud.China));
        File.WriteAllText(Path.Combine(_directory, "msal.cache"), "global-broker");
        File.WriteAllText(Path.Combine(_directory, "msal-devicecode.cache"), "global-devicecode");
        File.WriteAllText(Path.Combine(_directory, "msal-china.cache"), "china-broker");
        File.WriteAllText(Path.Combine(_directory, $"msal-devicecode-{ChinaAppId}.cache"), "already-there");
        File.WriteAllText(Path.Combine(_directory, "msal-devicecode-china.cache"), "china-devicecode");

        LegacyRegistrationMigration.Run(_configPath, _accountsPath, _directory);

        Assert.Equal("global-broker", File.ReadAllText(Path.Combine(_directory, $"msal-{GlobalAppId}.cache")));
        Assert.Equal("global-devicecode", File.ReadAllText(Path.Combine(_directory, $"msal-devicecode-{GlobalAppId}.cache")));
        Assert.Equal("china-broker", File.ReadAllText(Path.Combine(_directory, $"msal-{ChinaAppId}.cache")));
        Assert.False(File.Exists(Path.Combine(_directory, "msal.cache")));
        Assert.False(File.Exists(Path.Combine(_directory, "msal-china.cache")));

        // A target that already exists belongs to a registration the user added
        // before upgrading — never clobber it; the legacy file stays put.
        Assert.Equal("already-there", File.ReadAllText(Path.Combine(_directory, $"msal-devicecode-{ChinaAppId}.cache")));
        Assert.True(File.Exists(Path.Combine(_directory, "msal-devicecode-china.cache")));
    }

    [Fact]
    public void Run_NoLegacyKeys_LeavesTheFileUntouched()
    {
        const string current = """
            {
              "EntraPimManager": {
                "TenantAppRegistrations": [ { "TenantId": "7b1c4d2e-0000-4000-8000-0000000000aa", "ClientId": "8f3a1c2e-0000-4000-8000-000000000001" } ]
              }
            }
            """;
        WriteConfig(current);

        var result = LegacyRegistrationMigration.Run(_configPath, _accountsPath, _directory);

        Assert.Same(LegacyRegistrationMigration.Result.Nothing, result);
        Assert.Equal(current, File.ReadAllText(_configPath));
    }

    [Fact]
    public void Run_MissingConfigFile_IsANoOp()
        => Assert.Same(LegacyRegistrationMigration.Result.Nothing, LegacyRegistrationMigration.Run(_configPath, _accountsPath, _directory));

    [Fact]
    public void Run_ClientIdWithoutAnyTenant_IsReportedAsDroppedAndRemoved()
    {
        // Nothing to pin the id to — the caller tells the user to add it again.
        // The legacy key is still removed so the migration does not run forever.
        WriteConfig($$"""{ "EntraPimManager": { "AppRegistrations": { "Global": "{{GlobalAppId}}", "China": "" } } }""");

        var result = LegacyRegistrationMigration.Run(_configPath, _accountsPath, _directory);

        Assert.Equal([(EntraCloud.Global, GlobalAppId)], result.Dropped);
        Assert.Empty(result.Migrated);
        Assert.Empty(ReadEntries());
        Assert.False(JsonDocument.Parse(File.ReadAllText(_configPath)).RootElement.GetProperty("EntraPimManager").TryGetProperty("AppRegistrations", out _));
    }

    [Fact]
    public void Run_AllowedTenants_BecomeEntriesOfTheGlobalClientId()
    {
        // A whitelisted tenant was reachable without an enrollment; keep it so.
        WriteConfig($$"""
            {
              "EntraPimManager": {
                "AppRegistrations": { "Global": "{{GlobalAppId}}" },
                "AllowedTenants": [ "{{TenantB}}", "not-a-guid" ]
              }
            }
            """);
        WriteAccounts(Account("oid-1", TenantA, EntraCloud.Global));

        var result = LegacyRegistrationMigration.Run(_configPath, _accountsPath, _directory);

        Assert.Empty(result.Dropped);
        var entries = ReadEntries();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.TenantId == TenantB && e.ClientId == GlobalAppId);
        Assert.False(JsonDocument.Parse(File.ReadAllText(_configPath)).RootElement.GetProperty("EntraPimManager").TryGetProperty("AllowedTenants", out _));
    }

    [Fact]
    public void Run_LegacySingularClientId_CountsAsGlobal()
    {
        // Pre-0.4.2 files carry a bare ClientId — always a Global registration.
        WriteConfig($$"""{ "EntraPimManager": { "ClientId": "{{GlobalAppId}}" } }""");
        WriteAccounts(Account("oid-1", TenantA, EntraCloud.Global));

        LegacyRegistrationMigration.Run(_configPath, _accountsPath, _directory);

        var entry = Assert.Single(ReadEntries());
        Assert.Equal(GlobalAppId, entry.ClientId);
        Assert.False(JsonDocument.Parse(File.ReadAllText(_configPath)).RootElement.GetProperty("EntraPimManager").TryGetProperty("ClientId", out _));
    }

    [Fact]
    public void Run_KeepsAnEntryTheUserAlreadyAdded()
    {
        // The user pinned tenant A to their own registration before upgrading;
        // the legacy cloud-wide id must not overwrite that choice.
        WriteConfig($$"""
            {
              "EntraPimManager": {
                "AppRegistrations": { "Global": "{{GlobalAppId}}" },
                "TenantAppRegistrations": [ { "TenantId": "{{TenantA}}", "ClientId": "{{ChinaAppId}}", "Label": "Mine" } ]
              }
            }
            """);
        WriteAccounts(Account("oid-1", TenantA, EntraCloud.Global));

        var result = LegacyRegistrationMigration.Run(_configPath, _accountsPath, _directory);

        Assert.Empty(result.Migrated);
        Assert.Empty(result.Dropped);
        var entry = Assert.Single(ReadEntries());
        Assert.Equal(ChinaAppId, entry.ClientId);
        Assert.Equal("Mine", entry.Label);
    }

    private static string Account(string oid, string tenantId, EntraCloud cloud)
        => $$"""{ "ObjectId": "{{oid}}", "TenantId": "{{tenantId}}", "Username": "u@example.com", "DisplayName": null, "AddedAt": "2026-01-01T00:00:00+00:00", "Cloud": {{(int)cloud}} }""";

    private void WriteConfig(string json) => File.WriteAllText(_configPath, json);

    private void WriteAccounts(params string[] accounts)
        => File.WriteAllText(_accountsPath, "[" + string.Join(",", accounts) + "]");

    private List<TenantAppRegistration> ReadEntries()
    {
        var section = JsonDocument.Parse(File.ReadAllText(_configPath)).RootElement.GetProperty("EntraPimManager");
        return section.TryGetProperty("TenantAppRegistrations", out var list)
            ? list.Deserialize<List<TenantAppRegistration>>()!
            : [];
    }
}
