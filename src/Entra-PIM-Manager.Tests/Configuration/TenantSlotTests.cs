namespace EntraPimManager.Tests.Configuration;

using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;

/// <summary>
/// Covers the identity and ordering of the Settings tenant tree. A slot that fails to
/// collapse shows one tenant as two cards with half its accounts under each; a slot that
/// throws takes the whole Settings panel with it.
/// </summary>
public sealed class TenantSlotTests
{
    private const string TenantA = "7b1c4d2e-0000-4000-8000-0000000000aa";
    private const string TenantB = "7b1c4d2e-0000-4000-8000-0000000000bb";

    [Theory]
    [InlineData("7B1C4D2E-0000-4000-8000-0000000000AA")]
    [InlineData("{7b1c4d2e-0000-4000-8000-0000000000aa}")]
    [InlineData("(7b1c4d2e-0000-4000-8000-0000000000aa)")]
    [InlineData("  7b1c4d2e-0000-4000-8000-0000000000aa  ")]
    public void Key_IsTheSameForEveryWayOfWritingOneTenantId(string written)
    {
        // appsettings.local.json is hand-editable and MSAL reports its own casing —
        // all of these mean the same directory.
        var canonical = new TenantSlot(EntraCloud.Global, TenantA);
        var variant = new TenantSlot(EntraCloud.Global, written);

        Assert.Equal(canonical.Key, variant.Key);
    }

    [Fact]
    public void Key_SeparatesTheSameTenantIdInDifferentClouds()
    {
        Assert.NotEqual(
            new TenantSlot(EntraCloud.Global, TenantA).Key,
            new TenantSlot(EntraCloud.China, TenantA).Key);
    }

    [Fact]
    public void NormaliseTenantId_KeepsAValueThatIsNoGuid()
    {
        // A hand-edited file reaches this code before any validator could stop it;
        // one odd-looking card beats an exception in the settings panel.
        Assert.Equal("not-a-guid", TenantSlot.NormaliseTenantId("  Not-A-Guid  "));
        Assert.Equal(string.Empty, TenantSlot.NormaliseTenantId(null));
        Assert.Equal(string.Empty, TenantSlot.NormaliseTenantId("   "));
    }

    [Fact]
    public void Merge_CollapsesADuplicateSlotInsteadOfThrowing()
    {
        var registrations = new[]
        {
            new TenantSlot(EntraCloud.Global, TenantA),
            new TenantSlot(EntraCloud.Global, TenantA.ToUpperInvariant()),
        };

        var merged = TenantSlot.Merge([], registrations);

        Assert.Single(merged);
    }

    [Fact]
    public void Merge_ListsTenantsWithAccountsFirstInAccountOrder()
    {
        // The popup orders its eligibility groups by the same account collection —
        // sorting differently here would make the two views permanently disagree.
        var accounts = new[]
        {
            new TenantSlot(EntraCloud.Global, TenantB),
            new TenantSlot(EntraCloud.Global, TenantA),
        };
        var registrations = new[]
        {
            new TenantSlot(EntraCloud.Global, TenantA),
            new TenantSlot(EntraCloud.Global, TenantB),
        };

        var merged = TenantSlot.Merge(accounts, registrations);

        Assert.Equal(2, merged.Count);
        Assert.Equal(TenantB, merged[0].TenantId);
        Assert.Equal(TenantA, merged[1].TenantId);
    }

    [Fact]
    public void Merge_PutsAConfiguredTenantWithoutAnAccountLast()
    {
        var accounts = new[] { new TenantSlot(EntraCloud.Global, TenantA) };
        var registrations = new[] { new TenantSlot(EntraCloud.Global, TenantB) };

        var merged = TenantSlot.Merge(accounts, registrations);

        Assert.Equal(TenantA, merged[0].TenantId);
        Assert.Equal(TenantB, merged[1].TenantId);
    }

    [Fact]
    public void Merge_KeepsATenantThatHasAccountsButNoRegistration()
    {
        // Removing a registration deliberately leaves its accounts enrolled. Without
        // this, those accounts would have no card to sit under and vanish from Settings.
        var accounts = new[] { new TenantSlot(EntraCloud.Global, TenantA) };

        var merged = TenantSlot.Merge(accounts, []);

        Assert.Equal(TenantA, Assert.Single(merged).TenantId);
    }

    [Fact]
    public void Merge_EmptyInputYieldsEmpty()
    {
        Assert.Empty(TenantSlot.Merge([], []));
    }
}
