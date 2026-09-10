namespace EntraPimManager.Tests.Models;

using EntraPimManager.Core.Models;

/// <summary>
/// A scope favourite is matched to its eligibility and compared to the current
/// selection by these rules; getting either wrong offers a chip on the wrong role or
/// hides the save button behind a favourite that is not the same set.
/// </summary>
public sealed class ScopeFavoriteTests
{
    private const string RoleId = "/providers/Microsoft.Management/managementGroups/root/providers/Microsoft.Authorization/roleDefinitions/8e3af657-a8ff-443c-a75c-2fe8c4bcb635";
    private const string RootScope = "/providers/Microsoft.Management/managementGroups/root";

    [Fact]
    public void Label_JoinsTheScopeNamesInSavedOrder()
    {
        Assert.Equal("lz-prod, Landing zones", Favorite().Label);
    }

    [Fact]
    public void BelongsTo_MatchesTheEnrollmentAndEligibilityCaseInsensitively()
    {
        var favorite = Favorite() with { ObjectId = "oid-a" };

        Assert.True(favorite.BelongsTo("OID-A", "TENANT-A", RoleId.ToUpperInvariant(), RootScope.ToUpperInvariant()));
        Assert.False(favorite.BelongsTo("oid-a", "tenant-b", RoleId, RootScope));
        Assert.False(favorite.BelongsTo("oid-a", "tenant-a", RoleId, "/subscriptions/sub-prod"));

        // Two accounts can be enrolled in one tenant and hold the same role at the
        // same scope; a favourite of one must not activate under the other.
        Assert.False(favorite.BelongsTo("oid-b", "tenant-a", RoleId, RootScope));
    }

    [Fact]
    public void BelongsTo_WithoutAnObjectId_StillMatchesOnTheTenant()
    {
        // Entries written before the account was recorded keep working; dropping them
        // would silently delete favourites on upgrade.
        Assert.True(Favorite().BelongsTo("any-oid", "tenant-a", RoleId, RootScope));
    }

    [Fact]
    public void HasSameScopes_IgnoresOrderAndCasing()
    {
        var favorite = Favorite();

        Assert.True(favorite.HasSameScopes(["/PROVIDERS/Microsoft.Management/managementGroups/mg-lz", "/subscriptions/sub-prod"]));
        Assert.False(favorite.HasSameScopes(["/subscriptions/sub-prod"]));
        Assert.False(favorite.HasSameScopes(["/subscriptions/sub-prod", "/providers/Microsoft.Management/managementGroups/mg-lz", "/subscriptions/sub-dev"]));
    }

    [Fact]
    public void EligibleChildScope_LabelsManagementGroupsAndSubscriptions()
    {
        Assert.Equal("Subscription: lz-prod", new EligibleChildScope("/subscriptions/sub-prod", "lz-prod", "subscription").ScopeLabel);
        Assert.Equal("Management group: Landing zones", new EligibleChildScope("/providers/Microsoft.Management/managementGroups/mg-lz", "Landing zones", "ManagementGroup").ScopeLabel);

        // The same mapping labels the eligibility list's rows; a resource group can only
        // reach it through that path, never through the picker.
        Assert.Equal("Resource group", EligibleChildScope.KindLabelFor("resourcegroup"));
        Assert.Equal("Resource", EligibleChildScope.KindLabelFor(null));
    }

    [Fact]
    public void Label_PrefersTheNameAndKeepsTheScopesForTheTooltip()
    {
        var named = Favorite() with { Name = "Project X" };

        Assert.Equal("Project X", named.Label);
        Assert.Equal("lz-prod, Landing zones", named.ScopesLabel);
    }

    [Fact]
    public void IsPinned_DefaultsToOn_AndSurvivesAWithCopy()
    {
        // Saving a set is the deliberate act, so it lands on the main page; the star
        // takes it off again without deleting the favourite.
        var favorite = Favorite();

        Assert.True(favorite.IsPinned);
        Assert.False((favorite with { IsPinned = false }).IsPinned);
    }

    private static ScopeFavorite Favorite() => new(
        Guid.NewGuid(),
        "tenant-a",
        RoleId,
        RootScope,
        [
            new EligibleChildScope("/subscriptions/sub-prod", "lz-prod", "subscription"),
            new EligibleChildScope("/providers/Microsoft.Management/managementGroups/mg-lz", "Landing zones", "managementgroup"),
        ],
        DateTimeOffset.UtcNow);
}
