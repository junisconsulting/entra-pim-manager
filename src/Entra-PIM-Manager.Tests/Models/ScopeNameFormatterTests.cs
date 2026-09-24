namespace EntraPimManager.Tests.Models;

using EntraPimManager.Core.Models;
using Xunit;

public class ScopeNameFormatterTests
{
    private const string Subscription = "/subscriptions/11111111-1111-1111-1111-111111111111";
    private const string ResourceGroup = Subscription + "/resourceGroups/rg-prod";
    private const string ManagementGroup = "/providers/Microsoft.Management/managementGroups/mg-landing-zones";

    [Theory]
    [InlineData("Subscription: lz-adp-poc", "lz-adp-poc")]
    [InlineData("Management group: Tenant Root Group", "Tenant Root Group")]
    [InlineData("Subscription: Contoso: Production", "Contoso: Production")]
    [InlineData("noseparator", "noseparator")]
    public void NameOf_StripsTheKindPrefix(string label, string expected)
        => Assert.Equal(expected, ScopeNameFormatter.NameOf(label, Subscription));

    [Fact]
    public void NameOf_WithoutLabel_FallsBackToTheLastSegment()
        => Assert.Equal("rg-prod", ScopeNameFormatter.NameOf(null, ResourceGroup));

    [Fact]
    public void Describe_Subscription_IsItsOwnName()
        => Assert.Equal("lz-adp-poc", ScopeNameFormatter.Describe(Subscription, "Subscription: lz-adp-poc", null));

    [Fact]
    public void Describe_ManagementGroup_IsItsOwnName()
        => Assert.Equal(
            "mg-landing-zones",
            ScopeNameFormatter.Describe(ManagementGroup, "Management group: mg-landing-zones", null));

    [Fact]
    public void Describe_ResourceGroup_IsQualifiedByTheSubscriptionName()
        => Assert.Equal(
            "lz-adp-poc/rg-prod",
            ScopeNameFormatter.Describe(ResourceGroup, "Resource group: rg-prod", "lz-adp-poc"));

    [Fact]
    public void Describe_ResourceGroup_FallsBackToTheSubscriptionId()
    {
        // ARM does not return the subscription's display name on a resource-group
        // scoped assignment. The id is at least resolvable; the bare group name is
        // not, because the same one exists in half the subscriptions.
        var described = ScopeNameFormatter.Describe(ResourceGroup, "Resource group: rg-prod", null);

        Assert.Equal("11111111-1111-1111-1111-111111111111/rg-prod", described);
    }

    [Fact]
    public void Describe_ResourceInsideAGroup_IsNotQualified()
    {
        // "Resource: vm-01" identifies itself; prefixing a subscription would only
        // make the line longer.
        var scopeId = ResourceGroup + "/providers/Microsoft.Compute/virtualMachines/vm-01";

        Assert.Equal("vm-01", ScopeNameFormatter.Describe(scopeId, "Resource: vm-01", "lz-adp-poc"));
    }

    [Theory]
    [InlineData(Subscription, "11111111-1111-1111-1111-111111111111")]
    [InlineData(ResourceGroup, null)]
    [InlineData(ManagementGroup, null)]
    public void SubscriptionScopeOf_OnlyMatchesTheSubscriptionItself(string scopeId, string? expected)
        => Assert.Equal(expected, ScopeNameFormatter.SubscriptionScopeOf(scopeId));

    [Theory]
    [InlineData(ResourceGroup, "11111111-1111-1111-1111-111111111111")]
    [InlineData(Subscription, "11111111-1111-1111-1111-111111111111")]
    [InlineData(ManagementGroup, null)]
    [InlineData("/", null)]
    public void SubscriptionIdOf_ReadsTheSubscriptionSegment(string scopeId, string? expected)
        => Assert.Equal(expected, ScopeNameFormatter.SubscriptionIdOf(scopeId));
}
