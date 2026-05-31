namespace EntraPimManager.Tests.Auth;

using EntraPimManager.Core.Auth;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Tests for <see cref="AccountStore"/>: file IO, JSON round-trip, idempotent upsert,
/// graceful handling of missing/corrupt files, and removal semantics.
/// </summary>
public sealed class AccountStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public AccountStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Entra PIM Manager-AccountStore-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "accounts.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetAllAsync_WithNoFile_ReturnsEmpty()
    {
        var store = CreateStore();

        var accounts = await store.GetAllAsync();

        Assert.Empty(accounts);
    }

    [Fact]
    public async Task UpsertAsync_PersistsAcrossInstances()
    {
        var account = MakeAccount("oid-1");
        var first = CreateStore();
        await first.UpsertAsync(account);

        // New instance, same file — must see the persisted account.
        var second = CreateStore();
        var loaded = await second.GetAllAsync();

        var only = Assert.Single(loaded);
        Assert.Equal(account.ObjectId, only.ObjectId);
        Assert.Equal(account.Username, only.Username);
    }

    [Fact]
    public async Task UpsertAsync_SecondCallSameOid_PreservesAddedAt()
    {
        var original = MakeAccount("oid-1") with { AddedAt = DateTimeOffset.UtcNow.AddDays(-7) };
        var store = CreateStore();
        await store.UpsertAsync(original);

        // Same oid, different metadata, fresh AddedAt — only metadata should update.
        var updated = original with { DisplayName = "Updated Name", AddedAt = DateTimeOffset.UtcNow };
        await store.UpsertAsync(updated);

        var loaded = Assert.Single(await store.GetAllAsync());
        Assert.Equal("Updated Name", loaded.DisplayName);
        Assert.Equal(original.AddedAt, loaded.AddedAt);
    }

    [Fact]
    public async Task UpsertAsync_DifferentOids_StoresMultipleAccounts()
    {
        var store = CreateStore();
        await store.UpsertAsync(MakeAccount("oid-1"));
        await store.UpsertAsync(MakeAccount("oid-2", "22222222-2222-2222-2222-222222222222"));

        var accounts = await store.GetAllAsync();

        Assert.Equal(2, accounts.Count);
        Assert.Contains(accounts, a => a.ObjectId == "oid-1");
        Assert.Contains(accounts, a => a.ObjectId == "oid-2");
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsMatchingAccountCaseInsensitive()
    {
        var store = CreateStore();
        await store.UpsertAsync(MakeAccount("Abc-Def"));

        var found = await store.GetByIdAsync("abc-DEF", "11111111-1111-1111-1111-111111111111");

        Assert.NotNull(found);
        Assert.Equal("Abc-Def", found!.ObjectId);
    }

    [Fact]
    public async Task GetByIdAsync_WithUnknownOid_ReturnsNull()
    {
        var store = CreateStore();
        await store.UpsertAsync(MakeAccount("oid-1"));

        var found = await store.GetByIdAsync("oid-other", "11111111-1111-1111-1111-111111111111");

        Assert.Null(found);
    }

    [Fact]
    public async Task GetByIdAsync_SameOidDifferentTenants_ReturnsRequestedEnrollmentOnly()
    {
        var store = CreateStore();
        await store.UpsertAsync(MakeAccount("oid-shared", "tenant-home"));
        await store.UpsertAsync(MakeAccount("oid-shared", "tenant-guest"));

        var home = await store.GetByIdAsync("oid-shared", "tenant-home");
        var guest = await store.GetByIdAsync("oid-shared", "tenant-guest");

        Assert.NotNull(home);
        Assert.NotNull(guest);
        Assert.Equal("tenant-home", home!.TenantId);
        Assert.Equal("tenant-guest", guest!.TenantId);
    }

    [Fact]
    public async Task UpsertAsync_SameOidDifferentTenants_StoresBothEnrollments()
    {
        var store = CreateStore();
        await store.UpsertAsync(MakeAccount("oid-shared", "tenant-home"));
        await store.UpsertAsync(MakeAccount("oid-shared", "tenant-guest"));

        var all = await store.GetAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, a => a.TenantId == "tenant-home");
        Assert.Contains(all, a => a.TenantId == "tenant-guest");
    }

    [Fact]
    public async Task RemoveAsync_RemovesMatchingEntry()
    {
        var store = CreateStore();
        await store.UpsertAsync(MakeAccount("oid-1"));
        await store.UpsertAsync(MakeAccount("oid-2"));

        await store.RemoveAsync("oid-1", "11111111-1111-1111-1111-111111111111");

        var remaining = await store.GetAllAsync();
        Assert.Equal("oid-2", Assert.Single(remaining).ObjectId);
    }

    [Fact]
    public async Task RemoveAsync_SameOidDifferentTenant_OnlyRemovesMatchingEnrollment()
    {
        var store = CreateStore();
        await store.UpsertAsync(MakeAccount("oid-shared", "tenant-home"));
        await store.UpsertAsync(MakeAccount("oid-shared", "tenant-guest"));

        await store.RemoveAsync("oid-shared", "tenant-home");

        var remaining = Assert.Single(await store.GetAllAsync());
        Assert.Equal("tenant-guest", remaining.TenantId);
    }

    [Fact]
    public async Task RemoveAsync_WithUnknownOid_IsNoOp()
    {
        var store = CreateStore();
        await store.UpsertAsync(MakeAccount("oid-1"));

        await store.RemoveAsync("oid-other", "11111111-1111-1111-1111-111111111111");

        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task GetAllAsync_WithCorruptFile_ReturnsEmpty()
    {
        File.WriteAllText(_filePath, "{not valid json");
        var store = CreateStore();

        var accounts = await store.GetAllAsync();

        // Corrupt file is treated as empty; subsequent writes recover.
        Assert.Empty(accounts);
    }

    [Fact]
    public async Task UpsertAsync_AfterCorruptFile_RecoversWithNewContent()
    {
        File.WriteAllText(_filePath, "garbage");
        var store = CreateStore();

        await store.UpsertAsync(MakeAccount("oid-1"));

        var fresh = CreateStore();
        var only = Assert.Single(await fresh.GetAllAsync());
        Assert.Equal("oid-1", only.ObjectId);
    }

    [Fact]
    public async Task UpsertAsync_NonDefaultCloud_RoundTripsThroughJson()
    {
        var account = MakeAccount("oid-china") with { Cloud = EntraCloud.China };
        var first = CreateStore();
        await first.UpsertAsync(account);

        var second = CreateStore();
        var loaded = Assert.Single(await second.GetAllAsync());

        Assert.Equal(EntraCloud.China, loaded.Cloud);
        Assert.Equal(account.ObjectId, loaded.ObjectId);
    }

    [Fact]
    public async Task GetAllAsync_LegacyFileWithoutCloud_DefaultsToGlobal()
    {
        // Simulate a file written by a pre-Cloud build: no "Cloud" property.
        const string legacyJson = """
        [
          {
            "ObjectId": "oid-legacy",
            "TenantId": "11111111-1111-1111-1111-111111111111",
            "Username": "legacy@example.com",
            "DisplayName": "Legacy",
            "AddedAt": "2024-01-01T00:00:00+00:00"
          }
        ]
        """;
        await File.WriteAllTextAsync(_filePath, legacyJson);

        var store = CreateStore();
        var loaded = Assert.Single(await store.GetAllAsync());

        Assert.Equal(EntraCloud.Global, loaded.Cloud);
        Assert.Equal("oid-legacy", loaded.ObjectId);
    }

    [Fact]
    public async Task ReorderAsync_PersistsNewOrder()
    {
        var store = CreateStore();
        await store.UpsertAsync(MakeAccount("oid-a", "tenant-a"));
        await store.UpsertAsync(MakeAccount("oid-b", "tenant-b"));
        await store.UpsertAsync(MakeAccount("oid-c", "tenant-c"));

        var reordered = new[]
        {
            MakeAccount("oid-c", "tenant-c"),
            MakeAccount("oid-a", "tenant-a"),
            MakeAccount("oid-b", "tenant-b"),
        };
        await store.ReorderAsync(reordered);

        var fresh = CreateStore();
        var loaded = await fresh.GetAllAsync();

        Assert.Equal(3, loaded.Count);
        Assert.Equal("oid-c", loaded[0].ObjectId);
        Assert.Equal("oid-a", loaded[1].ObjectId);
        Assert.Equal("oid-b", loaded[2].ObjectId);
    }

    [Fact]
    public async Task ReorderAsync_PreservesAddedAtFromStoredEntries()
    {
        // The caller's view of AddedAt is likely stale; the store must keep
        // the original timestamps.
        var oldA = DateTimeOffset.UtcNow.AddDays(-5);
        var oldB = DateTimeOffset.UtcNow.AddDays(-2);
        var store = CreateStore();
        await store.UpsertAsync(MakeAccount("oid-a") with { AddedAt = oldA, TenantId = "tenant-a" });
        await store.UpsertAsync(MakeAccount("oid-b") with { AddedAt = oldB, TenantId = "tenant-b" });

        var fresh = new[]
        {
            MakeAccount("oid-b", "tenant-b") with { AddedAt = DateTimeOffset.UtcNow },
            MakeAccount("oid-a", "tenant-a") with { AddedAt = DateTimeOffset.UtcNow },
        };
        await store.ReorderAsync(fresh);

        var loaded = await store.GetAllAsync();
        Assert.Equal(oldB, loaded[0].AddedAt);
        Assert.Equal(oldA, loaded[1].AddedAt);
    }

    [Fact]
    public async Task ReorderAsync_AppendsUnmentionedStoredEntriesAtEnd()
    {
        // Defensive: a stale caller view must not silently drop enrollments
        // it doesn't know about.
        var store = CreateStore();
        await store.UpsertAsync(MakeAccount("oid-a", "tenant-a"));
        await store.UpsertAsync(MakeAccount("oid-b", "tenant-b"));
        await store.UpsertAsync(MakeAccount("oid-c", "tenant-c"));

        // oid-c not mentioned in partial — must survive at the tail.
        var partial = new[]
        {
            MakeAccount("oid-b", "tenant-b"),
            MakeAccount("oid-a", "tenant-a"),
        };
        await store.ReorderAsync(partial);

        var loaded = await store.GetAllAsync();
        Assert.Equal(3, loaded.Count);
        Assert.Equal("oid-b", loaded[0].ObjectId);
        Assert.Equal("oid-a", loaded[1].ObjectId);
        Assert.Equal("oid-c", loaded[2].ObjectId);
    }

    [Fact]
    public async Task ReorderAsync_IgnoresUnknownAccountsInPayload()
    {
        var store = CreateStore();
        await store.UpsertAsync(MakeAccount("oid-a", "tenant-a"));

        var payload = new[]
        {
            MakeAccount("oid-ghost", "tenant-ghost"),
            MakeAccount("oid-a", "tenant-a"),
        };
        await store.ReorderAsync(payload);

        var loaded = Assert.Single(await store.GetAllAsync());
        Assert.Equal("oid-a", loaded.ObjectId);
    }

    [Fact]
    public async Task ReorderAsync_EmptyStore_IsNoOp()
    {
        var store = CreateStore();
        await store.ReorderAsync(new[] { MakeAccount("oid-x", "tenant-x") });

        Assert.Empty(await store.GetAllAsync());
    }

    private static SignedInAccount MakeAccount(string oid, string tenant = "11111111-1111-1111-1111-111111111111")
        => new(
            ObjectId: oid,
            TenantId: tenant,
            Username: $"user-{oid}@example.com",
            DisplayName: $"User {oid}",
            AddedAt: DateTimeOffset.UtcNow);

    private AccountStore CreateStore() => new(_filePath, NullLogger<AccountStore>.Instance);
}
