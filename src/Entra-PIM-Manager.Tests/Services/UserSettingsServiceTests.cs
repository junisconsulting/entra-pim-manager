namespace EntraPimManager.Tests.Services;

using EntraPimManager.Core.Configuration;
using EntraPimManager.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Tests for <see cref="UserSettingsService"/>: defaults when the file is
/// missing or corrupt, round-trip persistence across instances, atomic write
/// (the temp file does not survive a successful save), and the
/// <see cref="IUserSettingsService.Changed"/> event firing on save.
/// </summary>
public sealed class UserSettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public UserSettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Entra PIM Manager-UserSettings-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Current_BeforeLoad_ReturnsDefaults()
    {
        var store = CreateStore();

        Assert.Same(UserSettings.Default, store.Current);
    }

    [Fact]
    public async Task LoadAsync_WithNoFile_LeavesDefaults()
    {
        var store = CreateStore();

        await store.LoadAsync();

        Assert.Equal(UserSettings.Default, store.Current);
        Assert.False(File.Exists(_filePath));
    }

    [Fact]
    public async Task LoadAsync_WithCorruptFile_FallsBackToDefaults()
    {
        await File.WriteAllTextAsync(_filePath, "{ this is not json");
        var store = CreateStore();

        await store.LoadAsync();

        Assert.Equal(UserSettings.Default, store.Current);
    }

    [Fact]
    public async Task SaveAsync_PersistsAcrossInstances()
    {
        var customised = new UserSettings(ThemePreference.Light, 4.0, false, 15);

        var first = CreateStore();
        await first.SaveAsync(customised);

        var second = CreateStore();
        await second.LoadAsync();

        Assert.Equal(customised, second.Current);
    }

    [Fact]
    public async Task SaveAsync_FiresChangedEvent()
    {
        var store = CreateStore();
        UserSettings? received = null;
        store.Changed += s => received = s;

        var newSettings = new UserSettings(ThemePreference.Dark, 2.0, true, 10);
        await store.SaveAsync(newSettings);

        Assert.NotNull(received);
        Assert.Equal(newSettings, received);
    }

    [Fact]
    public async Task SaveAsync_UpdatesCurrentInPlace()
    {
        var store = CreateStore();
        var newSettings = new UserSettings(ThemePreference.Dark, 8.0, true, 5);

        await store.SaveAsync(newSettings);

        Assert.Equal(newSettings, store.Current);
    }

    [Fact]
    public async Task SaveAsync_CreatesParentDirectoryIfMissing()
    {
        var nestedPath = Path.Combine(_tempDir, "nested", "deeper", "settings.json");
        var store = new UserSettingsService(nestedPath, NullLogger<UserSettingsService>.Instance);

        await store.SaveAsync(UserSettings.Default);

        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public async Task SaveAsync_DoesNotLeaveTempFileBehind()
    {
        var store = CreateStore();

        await store.SaveAsync(new UserSettings(ThemePreference.Light, 1.0, true, 5));

        Assert.False(File.Exists(_filePath + ".tmp"));
    }

    [Fact]
    public async Task SaveAsync_ThemePreferenceRoundTripsAsString()
    {
        var store = CreateStore();
        await store.SaveAsync(new UserSettings(ThemePreference.Light, 1.0, true, 5));

        var raw = await File.ReadAllTextAsync(_filePath);

        // Enum should serialise as the readable name, not the underlying int —
        // hand-editing the file is a supported scenario.
        Assert.Contains("\"Light\"", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_FileWithoutNewFields_StillLoads()
    {
        // Backwards-compat: a settings.json written before Phase 4 has no
        // LastUsedAccountKey / ExpandedTenants. It must deserialize cleanly
        // with the new fields at their null defaults.
        const string legacyJson = """
            {
              "Theme": "Dark",
              "DefaultDurationHours": 2.0,
              "ExpiryWarningEnabled": true,
              "ExpiryWarningMinutes": 10
            }
            """;
        await File.WriteAllTextAsync(_filePath, legacyJson);
        var store = CreateStore();

        await store.LoadAsync();

        Assert.Equal(ThemePreference.Dark, store.Current.Theme);
        Assert.Equal(2.0, store.Current.DefaultDurationHours);
        Assert.Null(store.Current.LastUsedAccountKey);
        Assert.Null(store.Current.ExpandedTenants);
    }

    [Fact]
    public async Task LoadAsync_LegacyFileWithoutSettingsAccountsExpanded_DefaultsToTrue()
    {
        // Backwards-compat for Phase 5: settings.json from before Phase 5 has
        // no SettingsAccountsExpanded field. It must default to expanded so
        // the user can still see the section after upgrading.
        const string legacyJson = """
            {
              "Theme": "System",
              "DefaultDurationHours": 1.0,
              "ExpiryWarningEnabled": true,
              "ExpiryWarningMinutes": 5,
              "LastUsedAccountKey": "oid|tid|Global"
            }
            """;
        await File.WriteAllTextAsync(_filePath, legacyJson);
        var store = CreateStore();

        await store.LoadAsync();

        Assert.True(store.Current.SettingsAccountsExpanded);
    }

    [Fact]
    public async Task SaveAsync_PersistsSettingsAccountsExpanded()
    {
        var collapsed = UserSettings.Default with { SettingsAccountsExpanded = false };

        var first = CreateStore();
        await first.SaveAsync(collapsed);

        var second = CreateStore();
        await second.LoadAsync();

        Assert.False(second.Current.SettingsAccountsExpanded);
    }

    [Fact]
    public async Task SaveAsync_PersistsLastUsedAccountKeyAndExpandedTenants()
    {
        var customised = new UserSettings(
            Theme: ThemePreference.Light,
            DefaultDurationHours: 4.0,
            ExpiryWarningEnabled: false,
            ExpiryWarningMinutes: 15,
            LastUsedAccountKey: "oid-1|tenant-1|Global",
            ExpandedTenants: new Dictionary<string, bool>
            {
                ["tenant-1"] = true,
                ["tenant-2"] = false,
            });

        var first = CreateStore();
        await first.SaveAsync(customised);

        var second = CreateStore();
        await second.LoadAsync();

        Assert.Equal("oid-1|tenant-1|Global", second.Current.LastUsedAccountKey);
        Assert.NotNull(second.Current.ExpandedTenants);
        Assert.True(second.Current.ExpandedTenants!["tenant-1"]);
        Assert.False(second.Current.ExpandedTenants!["tenant-2"]);
    }

    private UserSettingsService CreateStore()
        => new(_filePath, NullLogger<UserSettingsService>.Instance);
}
