namespace EntraPimManager.Tests.Services;

using EntraPimManager.Core.Models;
using EntraPimManager.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Tests for <see cref="ScopeFavoritesStore"/>: file IO in a file of its own, the
/// per-enrollment scoping the panel and the main page share, starring, removal, and
/// resilience to a missing or corrupt scope-favorites.json.
/// </summary>
public sealed class ScopeFavoritesStoreTests : IDisposable
{
    private const string OidA = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string OidB = "bbbbbbbb-1111-2222-3333-444444444444";
    private const string TenantA = "11111111-1111-1111-1111-111111111111";
    private const string RootScope = "/providers/Microsoft.Management/managementGroups/root";
    private const string OwnerId = RootScope + "/providers/Microsoft.Authorization/roleDefinitions/8e3af657-a8ff-443c-a75c-2fe8c4bcb635";

    private readonly string _tempDir;
    private readonly string _filePath;

    public ScopeFavoritesStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Entra PIM Manager-ScopeFavorites-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "scope-favorites.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_WithNoFile_LeavesTheStoreEmpty()
    {
        var store = CreateStore();

        await store.LoadAsync();

        Assert.Empty(store.Current);
    }

    [Fact]
    public async Task AddAsync_PersistsAcrossInstancesWithItsScopes()
    {
        var favorite = Favorite("Project X");
        await CreateStore().AddAsync(favorite);

        var reloaded = CreateStore();
        await reloaded.LoadAsync();

        var stored = Assert.Single(reloaded.Current);
        Assert.Equal(favorite.Id, stored.Id);
        Assert.Equal("Project X", stored.Name);
        Assert.True(stored.IsPinned);
        var scope = Assert.Single(stored.Scopes);
        Assert.Equal("/subscriptions/sub-1", scope.Id);
        Assert.Equal("Subscription: lz-prod", scope.ScopeLabel);
    }

    [Fact]
    public async Task ForRole_ReturnsOnlyTheEnrollmentsOwnSets_OldestFirst()
    {
        var store = CreateStore();
        var older = Favorite("first") with { CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5) };
        var newer = Favorite("second");
        var otherAccount = Favorite("theirs") with { ObjectId = OidB };
        var otherRole = Favorite("other role") with { ResourceId = OwnerId + "-x" };
        await store.AddAsync(newer);
        await store.AddAsync(older);
        await store.AddAsync(otherAccount);
        await store.AddAsync(otherRole);

        var mine = store.ForRole(OidA, TenantA, OwnerId, RootScope).ToList();

        Assert.Equal(["first", "second"], mine.Select(favorite => favorite.Name));
    }

    [Fact]
    public async Task SetPinnedAsync_FlipsTheStarAndAnnouncesIt()
    {
        var store = CreateStore();
        var favorite = Favorite("Project X");
        await store.AddAsync(favorite);
        var announcements = 0;
        store.Changed += () => announcements++;

        await store.SetPinnedAsync(favorite.Id, isPinned: false);

        Assert.False(Assert.Single(store.Current).IsPinned);
        Assert.Equal(1, announcements);

        var reloaded = CreateStore();
        await reloaded.LoadAsync();
        Assert.False(Assert.Single(reloaded.Current).IsPinned);
    }

    [Fact]
    public async Task RemoveAsync_DropsTheSet_AndIsANoOpForAnUnknownId()
    {
        var store = CreateStore();
        var favorite = Favorite("Project X");
        await store.AddAsync(favorite);

        await store.RemoveAsync(Guid.NewGuid());
        Assert.Single(store.Current);

        await store.RemoveAsync(favorite.Id);
        Assert.Empty(store.Current);
    }

    [Fact]
    public async Task LoadAsync_WithACorruptFile_ResetsInsteadOfThrowing()
    {
        // The whole point of the separate file: a favourite nobody can read costs the
        // favourites and nothing else — settings.json is not even opened.
        await File.WriteAllTextAsync(_filePath, "{ not json");
        var store = CreateStore();

        await store.LoadAsync();

        Assert.Empty(store.Current);
    }

    [Fact]
    public async Task LoadAsync_WithAnEntryWhoseScopesAreNull_DropsThatEntry()
    {
        // Valid JSON, so the catch never fires — the entry reaches the list with a null
        // Scopes and every chip that renders it reads Scopes. Dropping it here is what
        // keeps a hand-edited file from taking down the UI thread.
        const string Json =
            """[{"Id":"11111111-1111-1111-1111-111111111111","TenantId":"t","ResourceId":"r","ScopeId":"s","Scopes":null,"CreatedAt":"2026-01-01T00:00:00+00:00"}]""";

        await File.WriteAllTextAsync(_filePath, Json);
        var store = CreateStore();

        await store.LoadAsync();

        Assert.Empty(store.Current);
    }

    [Fact]
    public async Task AddAsync_LeavesNoTempFileBehind()
    {
        await CreateStore().AddAsync(Favorite("Project X"));

        Assert.True(File.Exists(_filePath));
        Assert.False(File.Exists(_filePath + ".tmp"));
    }

    private static ScopeFavorite Favorite(string name) => new(
        Guid.NewGuid(),
        TenantA,
        OwnerId,
        RootScope,
        [new EligibleChildScope("/subscriptions/sub-1", "lz-prod", "subscription")],
        DateTimeOffset.UtcNow,
        name,
        OidA);

    private ScopeFavoritesStore CreateStore()
        => new(_filePath, NullLogger<ScopeFavoritesStore>.Instance);
}
