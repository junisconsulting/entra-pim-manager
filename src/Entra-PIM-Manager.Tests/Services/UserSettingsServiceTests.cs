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
    public async Task SaveAsync_PublishesCurrentBeforeTheWriteFinishes()
    {
        // Most callers save fire-and-forget and compose their next change from Current
        // moments later. If Current only caught up after the file write, that next
        // change would be built on the previous value and silently revert this one.
        var store = CreateStore();
        var saved = UserSettings.Default with { ExpiryWarningMinutes = 15 };

        var pending = store.SaveAsync(saved);
        var seenWhileWriting = store.Current.ExpiryWarningMinutes;
        await pending;

        Assert.Equal(15, seenWhileWriting);
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
    public void Default_HasAutomaticUpdatesEnabled()
    {
        // Auto-update is on out of the box; the Settings toggle lets users opt out.
        Assert.True(UserSettings.Default.AutomaticUpdatesEnabled);
    }

    [Fact]
    public async Task LoadAsync_LegacyFileWithoutAutomaticUpdatesEnabled_DefaultsToTrue()
    {
        // Backwards-compat: a settings.json written before the auto-update feature
        // has no AutomaticUpdatesEnabled field. It must default to enabled so
        // existing installs start checking for updates after upgrading.
        const string legacyJson = """
            {
              "Theme": "System",
              "DefaultDurationHours": 1.0,
              "ExpiryWarningEnabled": true,
              "ExpiryWarningMinutes": 5
            }
            """;
        await File.WriteAllTextAsync(_filePath, legacyJson);
        var store = CreateStore();

        await store.LoadAsync();

        Assert.True(store.Current.AutomaticUpdatesEnabled);
    }

    [Fact]
    public async Task SaveAsync_PersistsAutomaticUpdatesEnabled()
    {
        var disabled = UserSettings.Default with { AutomaticUpdatesEnabled = false };

        var first = CreateStore();
        await first.SaveAsync(disabled);

        var second = CreateStore();
        await second.LoadAsync();

        Assert.False(second.Current.AutomaticUpdatesEnabled);
    }

    [Fact]
    public async Task LoadAsync_LegacyFileWithoutVerifiedClientIds_LeavesItUnset()
    {
        // Backwards-compat: a settings.json written before the App Registration
        // verification feature has no VerifiedClientIds. It must stay null so the
        // shell's one-time backfill can adopt the working client ids instead of a
        // stale value being treated as proof.
        const string legacyJson = """
            {
              "Theme": "System",
              "DefaultDurationHours": 1.0,
              "ExpiryWarningEnabled": true,
              "ExpiryWarningMinutes": 5
            }
            """;
        await File.WriteAllTextAsync(_filePath, legacyJson);
        var store = CreateStore();

        await store.LoadAsync();

        Assert.Null(store.Current.VerifiedClientIds);
    }

    [Fact]
    public async Task LoadAsync_PreV042FileWithSingularVerifiedClientId_LeavesItUnset()
    {
        // The pre-0.4.2 singular `VerifiedClientId` is deliberately not migrated —
        // per-cloud registrations made verification a set. The badge falls back to
        // "configured, not verified" once and heals on the next sign-in.
        const string legacyJson = """
            {
              "Theme": "System",
              "DefaultDurationHours": 1.0,
              "ExpiryWarningEnabled": true,
              "ExpiryWarningMinutes": 5,
              "VerifiedClientId": "8f3a1c2e-0000-4000-8000-000000000001"
            }
            """;
        await File.WriteAllTextAsync(_filePath, legacyJson);
        var store = CreateStore();

        await store.LoadAsync();

        Assert.Null(store.Current.VerifiedClientIds);
    }

    [Fact]
    public async Task SaveAsync_PersistsVerifiedClientIdsForEveryCloud()
    {
        string[] ids =
        [
            "8f3a1c2e-0000-4000-8000-000000000001",
            "8f3a1c2e-0000-4000-8000-000000000002",
        ];
        var verified = UserSettings.Default with { VerifiedClientIds = ids };

        var first = CreateStore();
        await first.SaveAsync(verified);

        var second = CreateStore();
        await second.LoadAsync();

        Assert.Equal(ids, second.Current.VerifiedClientIds);
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

    [Fact]
    public async Task LoadAsync_LegacyFileWithoutLogLevel_DefaultsToInformation()
    {
        // Backwards-compat: a settings.json written before the log-level
        // setting has no LogLevel field. It must default to Information so
        // existing installs stop writing the MSAL debug volume after upgrading.
        const string legacyJson = """
            {
              "Theme": "System",
              "DefaultDurationHours": 1.0,
              "ExpiryWarningEnabled": true,
              "ExpiryWarningMinutes": 5
            }
            """;
        await File.WriteAllTextAsync(_filePath, legacyJson);
        var store = CreateStore();

        await store.LoadAsync();

        Assert.Equal(LogLevelPreference.Information, store.Current.LogLevel);
    }

    [Fact]
    public async Task SaveAsync_PersistsLogLevel()
    {
        var verbose = UserSettings.Default with { LogLevel = LogLevelPreference.Debug };

        var first = CreateStore();
        await first.SaveAsync(verbose);

        var second = CreateStore();
        await second.LoadAsync();

        Assert.Equal(LogLevelPreference.Debug, second.Current.LogLevel);
    }

    [Fact]
    public async Task SaveAsync_PersistsAccountAliasesPerEnrollment()
    {
        // Asserted key by key: UserSettings is a record, so its Dictionary member
        // compares by reference and whole-record equality would never hold across
        // a round-trip.
        var aliased = UserSettings.Default with
        {
            AccountAliases = new Dictionary<string, string>
            {
                ["oid-1|tenant-1|Global"] = "EADM",
                ["oid-1|tenant-2|Global"] = "Guest",
            },
        };

        var first = CreateStore();
        await first.SaveAsync(aliased);

        var second = CreateStore();
        await second.LoadAsync();

        Assert.NotNull(second.Current.AccountAliases);
        Assert.Equal("EADM", second.Current.AccountAliases!["oid-1|tenant-1|Global"]);
        Assert.Equal("Guest", second.Current.AccountAliases!["oid-1|tenant-2|Global"]);
    }

    [Fact]
    public async Task SaveAsync_PersistsPinnedAndRecentEligibilities()
    {
        var shortcuts = UserSettings.Default with
        {
            PinnedEligibilities = ["oid-1|tenant-1|Global|3|role-a|/subscriptions/sub-1"],
            RecentEligibilities = ["oid-1|tenant-1|Global|0|role-b|/", "oid-1|tenant-1|Global|3|role-a|/subscriptions/sub-1"],
        };

        var first = CreateStore();
        await first.SaveAsync(shortcuts);

        var second = CreateStore();
        await second.LoadAsync();

        Assert.Equal(shortcuts.PinnedEligibilities, second.Current.PinnedEligibilities);

        // Order carries the meaning here — the most recent activation comes first.
        Assert.Equal(shortcuts.RecentEligibilities, second.Current.RecentEligibilities);
    }

    [Fact]
    public async Task SaveAsync_PersistsTicketSystemsPerTenant()
    {
        var configured = UserSettings.Default with
        {
            TicketSystems = new Dictionary<string, string>
            {
                ["tenant-1"] = "ServiceNow",
                ["tenant-2"] = "Jira",
            },
        };

        var first = CreateStore();
        await first.SaveAsync(configured);

        var second = CreateStore();
        await second.LoadAsync();

        Assert.NotNull(second.Current.TicketSystems);
        Assert.Equal("ServiceNow", second.Current.TicketSystemFor("tenant-1"));
        Assert.Equal("Jira", second.Current.TicketSystemFor("tenant-2"));
    }

    [Fact]
    public void TicketSystemFor_UnknownOrUnconfiguredTenant_ReturnsNull()
    {
        Assert.Null(UserSettings.Default.TicketSystemFor("tenant-1"));
        Assert.Null(UserSettings.Default.TicketSystemFor(null));

        var configured = UserSettings.Default with
        {
            TicketSystems = new Dictionary<string, string> { ["tenant-1"] = "ServiceNow" },
        };
        Assert.Null(configured.TicketSystemFor("tenant-2"));

        // A GUID's casing differs between the Settings form, MSAL and a hand-edited
        // file; the deserialized dictionary is case-sensitive, so the lookup must not be.
        Assert.Equal("ServiceNow", configured.TicketSystemFor("TENANT-1"));
    }

    [Fact]
    public async Task LoadAsync_LegacyFileWithoutAccountAliases_LeavesItUnset()
    {
        // Backwards-compat: a settings.json written before per-account aliases has
        // no AccountAliases. It must stay null so every row falls back to the UPN.
        const string legacyJson = """
            {
              "Theme": "System",
              "DefaultDurationHours": 1.0,
              "ExpiryWarningEnabled": true,
              "ExpiryWarningMinutes": 5
            }
            """;
        await File.WriteAllTextAsync(_filePath, legacyJson);
        var store = CreateStore();

        await store.LoadAsync();

        Assert.Null(store.Current.AccountAliases);
    }

    private UserSettingsService CreateStore()
        => new(_filePath, NullLogger<UserSettingsService>.Instance);
}
