namespace EntraPimManager.Tests.Services;

using EntraPimManager.Core.Configuration;
using EntraPimManager.Core.Services;
using EntraPimManager.Tests.TestSupport;
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
    public async Task UpdateAsync_PublishesCurrentBeforeTheWriteFinishes()
    {
        // Most callers save fire-and-forget and compose their next change from Current
        // moments later. If Current only caught up after the file write, that next
        // change would be built on the previous value and silently revert this one.
        var store = CreateStore();
        var saved = UserSettings.Default with { ExpiryWarningMinutes = 15 };

        var pending = store.UpdateAsync(_ => saved);
        var seenWhileWriting = store.Current.ExpiryWarningMinutes;
        await pending;

        Assert.Equal(15, seenWhileWriting);
    }

    [Fact]
    public async Task UpdateAsync_TwoQuickUpdates_KeepBothChanges()
    {
        // After a sign-in the shell writes the last-used account and the verified registration
        // back to back. Built from Current outside the lock, the second write was composed from
        // the snapshot before the first and dropped it (field test 2026-10-17).
        var store = CreateStore();

        // Holds the lock while it writes, so both updates below queue behind it.
        var writing = store.UpdateAsync(_ => UserSettings.Default with { ExpiryWarningMinutes = 15 });
        var first = store.UpdateAsync(s => s with { LastUsedAccountKey = "oid|tid|Global" });
        var second = store.UpdateAsync(s => s with { VerifiedClientIds = ["8f3a1c2e-0000-4000-8000-000000000001"] });
        await Task.WhenAll(writing, first, second);

        var reloaded = CreateStore();
        await reloaded.LoadAsync();
        Assert.Equal(15, reloaded.Current.ExpiryWarningMinutes);
        Assert.Equal("oid|tid|Global", reloaded.Current.LastUsedAccountKey);
        Assert.Equal(["8f3a1c2e-0000-4000-8000-000000000001"], reloaded.Current.VerifiedClientIds!);
        Assert.Equal("oid|tid|Global", store.Current.LastUsedAccountKey);
        Assert.NotNull(store.Current.VerifiedClientIds);
    }

    [Fact]
    public async Task UpdateAsync_TransformThatChangesNothing_DoesNotWrite()
    {
        var store = CreateStore();
        var fired = false;
        store.Changed += _ => fired = true;

        await store.UpdateAsync(s => s);

        Assert.False(fired);
        Assert.False(File.Exists(_filePath));
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
    public async Task UpdateAsync_PersistsAcrossInstances()
    {
        var customised = new UserSettings(ThemePreference.Light, 4.0, false, 15);

        var first = CreateStore();
        await first.UpdateAsync(_ => customised);

        var second = CreateStore();
        await second.LoadAsync();

        Assert.Equal(customised, second.Current);
    }

    [Fact]
    public async Task UpdateAsync_FiresChangedEvent()
    {
        var store = CreateStore();
        UserSettings? received = null;
        store.Changed += s => received = s;

        var newSettings = new UserSettings(ThemePreference.Dark, 2.0, true, 10);
        await store.UpdateAsync(_ => newSettings);

        Assert.NotNull(received);
        Assert.Equal(newSettings, received);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesCurrentInPlace()
    {
        var store = CreateStore();
        var newSettings = new UserSettings(ThemePreference.Dark, 8.0, true, 5);

        await store.UpdateAsync(_ => newSettings);

        Assert.Equal(newSettings, store.Current);
    }

    [Fact]
    public async Task UpdateAsync_CreatesParentDirectoryIfMissing()
    {
        var nestedPath = Path.Combine(_tempDir, "nested", "deeper", "settings.json");
        var store = new UserSettingsService(nestedPath, NullLogger<UserSettingsService>.Instance);

        await store.UpdateAsync(_ => UserSettings.Default with { ExpiryWarningMinutes = 15 });

        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public async Task UpdateAsync_DoesNotLeaveTempFileBehind()
    {
        var store = CreateStore();

        await store.UpdateAsync(_ => new UserSettings(ThemePreference.Light, 1.0, true, 5));

        Assert.False(File.Exists(_filePath + ".tmp"));
    }

    [Fact]
    public async Task UpdateAsync_ThemePreferenceRoundTripsAsString()
    {
        var store = CreateStore();
        await store.UpdateAsync(_ => new UserSettings(ThemePreference.Light, 1.0, true, 5));

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
    public async Task UpdateAsync_PersistsAutomaticUpdatesEnabled()
    {
        var disabled = UserSettings.Default with { AutomaticUpdatesEnabled = false };

        var first = CreateStore();
        await first.UpdateAsync(_ => disabled);

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
    public async Task UpdateAsync_PersistsVerifiedClientIdsForEveryCloud()
    {
        string[] ids =
        [
            "8f3a1c2e-0000-4000-8000-000000000001",
            "8f3a1c2e-0000-4000-8000-000000000002",
        ];
        var verified = UserSettings.Default with { VerifiedClientIds = ids };

        var first = CreateStore();
        await first.UpdateAsync(_ => verified);

        var second = CreateStore();
        await second.LoadAsync();

        Assert.Equal(ids, second.Current.VerifiedClientIds);
    }

    [Fact]
    public async Task UpdateAsync_PersistsSettingsAccountsExpanded()
    {
        var collapsed = UserSettings.Default with { SettingsAccountsExpanded = false };

        var first = CreateStore();
        await first.UpdateAsync(_ => collapsed);

        var second = CreateStore();
        await second.LoadAsync();

        Assert.False(second.Current.SettingsAccountsExpanded);
    }

    [Fact]
    public async Task UpdateAsync_PersistsLastUsedAccountKeyAndExpandedTenants()
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
        await first.UpdateAsync(_ => customised);

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
    public async Task UpdateAsync_PersistsLogLevel()
    {
        var verbose = UserSettings.Default with { LogLevel = LogLevelPreference.Debug };

        var first = CreateStore();
        await first.UpdateAsync(_ => verbose);

        var second = CreateStore();
        await second.LoadAsync();

        Assert.Equal(LogLevelPreference.Debug, second.Current.LogLevel);
    }

    [Fact]
    public async Task UpdateAsync_PersistsAccountAliasesPerEnrollment()
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
        await first.UpdateAsync(_ => aliased);

        var second = CreateStore();
        await second.LoadAsync();

        Assert.NotNull(second.Current.AccountAliases);
        Assert.Equal("EADM", second.Current.AccountAliases!["oid-1|tenant-1|Global"]);
        Assert.Equal("Guest", second.Current.AccountAliases!["oid-1|tenant-2|Global"]);
    }

    [Fact]
    public async Task UpdateAsync_PersistsPinnedAndRecentEligibilities()
    {
        var shortcuts = UserSettings.Default with
        {
            PinnedEligibilities = ["oid-1|tenant-1|Global|3|role-a|/subscriptions/sub-1"],
            RecentEligibilities = ["oid-1|tenant-1|Global|0|role-b|/", "oid-1|tenant-1|Global|3|role-a|/subscriptions/sub-1"],
        };

        var first = CreateStore();
        await first.UpdateAsync(_ => shortcuts);

        var second = CreateStore();
        await second.LoadAsync();

        Assert.Equal(shortcuts.PinnedEligibilities, second.Current.PinnedEligibilities);

        // Order carries the meaning here — the most recent activation comes first.
        Assert.Equal(shortcuts.RecentEligibilities, second.Current.RecentEligibilities);
    }

    [Fact]
    public async Task UpdateAsync_PersistsTicketSystemsPerTenant()
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
        await first.UpdateAsync(_ => configured);

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
    public void WithTicketSystem_SetsReplacesAndClearsCaseInsensitively()
    {
        // The key as the deserializer leaves it (case-sensitive) and in a different casing
        // than the one the Settings form passes.
        var configured = UserSettings.Default with
        {
            TicketSystems = new Dictionary<string, string> { ["TENANT-1"] = "ServiceNow", ["tenant-2"] = "Jira" },
        };

        var replaced = configured.WithTicketSystem("tenant-1", "Remedy");
        Assert.Equal("Remedy", replaced.TicketSystemFor("tenant-1"));
        Assert.Equal(2, replaced.TicketSystems!.Count);

        var cleared = replaced.WithTicketSystem("Tenant-1", null);
        Assert.Null(cleared.TicketSystemFor("tenant-1"));
        Assert.Equal("Jira", cleared.TicketSystemFor("tenant-2"));

        Assert.Equal("Jira", UserSettings.Default.WithTicketSystem("tenant-2", "Jira").TicketSystemFor("TENANT-2"));
    }

    [Fact]
    public void WithTicketSystem_NothingToChange_ReturnsTheSameInstance()
    {
        // UpdateAsync skips the write only for the same instance: saving an unchanged card,
        // or removing a tenant that has no ticket system, must not rewrite settings.json.
        var configured = UserSettings.Default with
        {
            TicketSystems = new Dictionary<string, string> { ["tenant-1"] = "ServiceNow" },
        };

        Assert.Same(configured, configured.WithTicketSystem("TENANT-1", "ServiceNow"));
        Assert.Same(configured, configured.WithTicketSystem("tenant-2", null));
        Assert.Same(UserSettings.Default, UserSettings.Default.WithTicketSystem("tenant-1", null));
    }

    [Fact]
    public void WithTicketSystem_HandEditedDuplicateKeys_DoesNotThrow()
    {
        // A hand-edited settings.json can carry one tenant in two casings; a case-insensitive
        // copy constructor would throw on that and lose the save.
        var duplicated = UserSettings.Default with
        {
            TicketSystems = new Dictionary<string, string> { ["tenant-1"] = "ServiceNow", ["TENANT-1"] = "Jira" },
        };

        var cleared = duplicated.WithTicketSystem("tenant-1", null);

        Assert.Null(cleared.TicketSystemFor("tenant-1"));
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

    [Fact]
    public async Task LoadAsync_WhileFileIsLocked_ShowsDefaultsAndTheNextUpdateKeepsTheFile()
    {
        // A lock at logon used to cache the defaults, and the first save of the session wrote
        // them over aliases, pins, ticket systems and the rest.
        await CreateStore().UpdateAsync(_ => new UserSettings(ThemePreference.Light, 4.0, false, 15));

        var store = CreateStore();
        using (LockedFile.Hold(_filePath))
        {
            await store.LoadAsync();
        }

        Assert.Equal(UserSettings.Default.Theme, store.Current.Theme);

        await store.UpdateAsync(s => s with { ExpiryWarningMinutes = 20 });

        var fresh = CreateStore();
        await fresh.LoadAsync();
        Assert.Equal(ThemePreference.Light, fresh.Current.Theme);
        Assert.Equal(4.0, fresh.Current.DefaultDurationHours);
        Assert.Equal(20, fresh.Current.ExpiryWarningMinutes);
    }

    [Fact]
    public async Task UpdateAsync_WhileTheUnreadFileIsStillLocked_ThrowsAndKeepsIt()
    {
        await CreateStore().UpdateAsync(_ => new UserSettings(ThemePreference.Light, 4.0, false, 15));

        var store = CreateStore();
        using (LockedFile.Hold(_filePath))
        {
            await store.LoadAsync();
            await Assert.ThrowsAnyAsync<IOException>(
                () => store.UpdateAsync(s => s with { ExpiryWarningMinutes = 20 }));
            Assert.False(File.Exists(_filePath + ".tmp"));
        }

        var fresh = CreateStore();
        await fresh.LoadAsync();
        Assert.Equal(ThemePreference.Light, fresh.Current.Theme);
        Assert.Equal(15, fresh.Current.ExpiryWarningMinutes);
    }

    private UserSettingsService CreateStore()
        => new(_filePath, NullLogger<UserSettingsService>.Instance);
}
