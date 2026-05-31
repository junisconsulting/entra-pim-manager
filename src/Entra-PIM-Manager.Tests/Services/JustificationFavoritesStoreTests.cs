namespace EntraPimManager.Tests.Services;

using EntraPimManager.Core.Models;
using EntraPimManager.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Tests for <see cref="JustificationFavoritesStore"/>: file IO, per-role
/// scoping, duplicate suppression, removal, and resilience to a missing or
/// corrupt favourites.json.
/// </summary>
public sealed class JustificationFavoritesStoreTests : IDisposable
{
    private const string TenantA = "11111111-1111-1111-1111-111111111111";
    private const string TenantB = "22222222-2222-2222-2222-222222222222";
    private const string RoleA = "62e90394-69f5-4237-9190-012177145e10";
    private const string RoleB = "9a5d68dd-52b0-4cc2-bd40-abcf44900527";
    private const string TenantScope = "/";

    private readonly string _tempDir;
    private readonly string _filePath;

    public JustificationFavoritesStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Entra PIM Manager-FavoritesStore-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "favorites.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetForRoleAsync_WithNoFile_ReturnsEmpty()
    {
        var store = CreateStore();

        var favorites = await store.GetForRoleAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope);

        Assert.Empty(favorites);
    }

    [Fact]
    public async Task AddAsync_PersistsAcrossInstances()
    {
        var first = CreateStore();
        var saved = await first.AddAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope, "Incident IR-1234");

        var second = CreateStore();
        var loaded = Assert.Single(await second.GetForRoleAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope));

        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal("Incident IR-1234", loaded.Text);
    }

    [Fact]
    public async Task AddAsync_TrimsLeadingAndTrailingWhitespace()
    {
        var store = CreateStore();

        var saved = await store.AddAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope, "  weekly audit  ");

        Assert.Equal("weekly audit", saved.Text);
    }

    [Fact]
    public async Task AddAsync_DuplicateTextForSameRole_ReturnsExistingEntry()
    {
        var store = CreateStore();
        var first = await store.AddAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope, "Same text");

        var second = await store.AddAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope, "Same text");

        Assert.Equal(first.Id, second.Id);
        var loaded = await store.GetForRoleAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope);
        Assert.Single(loaded);
    }

    [Fact]
    public async Task GetForRoleAsync_OnlyReturnsMatchingRole()
    {
        var store = CreateStore();
        await store.AddAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope, "for role A");
        await store.AddAsync(TenantA, PimResourceKind.DirectoryRole, RoleB, TenantScope, "for role B");

        var forA = await store.GetForRoleAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope);

        var only = Assert.Single(forA);
        Assert.Equal("for role A", only.Text);
    }

    [Fact]
    public async Task GetForRoleAsync_OnlyReturnsMatchingTenant()
    {
        var store = CreateStore();
        await store.AddAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope, "tenant A reasoning");
        await store.AddAsync(TenantB, PimResourceKind.DirectoryRole, RoleA, TenantScope, "tenant B reasoning");

        var forA = await store.GetForRoleAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope);

        var only = Assert.Single(forA);
        Assert.Equal("tenant A reasoning", only.Text);
    }

    [Fact]
    public async Task GetForRoleAsync_OnlyReturnsMatchingKind()
    {
        var store = CreateStore();
        await store.AddAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope, "for directory role");
        await store.AddAsync(TenantA, PimResourceKind.GroupMembership, RoleA, TenantScope, "for group membership");

        var dirRoles = await store.GetForRoleAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope);

        var only = Assert.Single(dirRoles);
        Assert.Equal("for directory role", only.Text);
    }

    [Fact]
    public async Task GetForRoleAsync_OrdersByCreatedAt()
    {
        var store = CreateStore();
        var first = await store.AddAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope, "first");
        await Task.Delay(15);
        var second = await store.AddAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope, "second");

        var loaded = await store.GetForRoleAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope);

        Assert.Collection(
            loaded,
            f => Assert.Equal(first.Id, f.Id),
            f => Assert.Equal(second.Id, f.Id));
    }

    [Fact]
    public async Task RemoveAsync_DeletesOnlyMatchingEntry()
    {
        var store = CreateStore();
        var keep = await store.AddAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope, "keep me");
        var drop = await store.AddAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope, "drop me");

        await store.RemoveAsync(drop.Id);

        var loaded = await store.GetForRoleAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope);
        var only = Assert.Single(loaded);
        Assert.Equal(keep.Id, only.Id);
    }

    [Fact]
    public async Task RemoveAsync_NonexistentId_IsNoOp()
    {
        var store = CreateStore();
        await store.AddAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope, "keep");

        await store.RemoveAsync(Guid.NewGuid());

        var loaded = await store.GetForRoleAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope);
        Assert.Single(loaded);
    }

    [Fact]
    public async Task GetForRoleAsync_CorruptFile_ReturnsEmptyInsteadOfThrowing()
    {
        await File.WriteAllTextAsync(_filePath, "{not valid json");

        var store = CreateStore();
        var loaded = await store.GetForRoleAsync(TenantA, PimResourceKind.DirectoryRole, RoleA, TenantScope);

        Assert.Empty(loaded);
    }

    private JustificationFavoritesStore CreateStore() =>
        new(_filePath, NullLogger<JustificationFavoritesStore>.Instance);
}
