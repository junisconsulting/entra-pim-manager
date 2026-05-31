namespace EntraPimManager.Tests.Services;

using EntraPimManager.Core.Caching;
using EntraPimManager.Core.Models;
using EntraPimManager.Core.Services;
using EntraPimManager.Tests.TestSupport;

public sealed class PolicyServiceTests
{
    private const string TenantA = "11111111-1111-1111-1111-111111111111";
    private const string TenantB = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task GetPolicyAsync_ParsesEndUserRulesAndIgnoresAdminRules()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("policy-directory-full.json")));
        var service = new PolicyService(GraphClientTestBuilder.Build(handler), new PolicyCache());

        var policy = await service.GetPolicyAsync(TenantA, PimResourceKind.DirectoryRole, "role-def-ga");

        // The end-user expiration rule is PT4H; the admin-eligibility rule (P365D)
        // shares the same .NET type and must not leak into the parsed policy.
        Assert.Equal(TimeSpan.FromHours(4), policy.MaximumDuration);
        Assert.True(policy.RequiresJustification);
        Assert.True(policy.RequiresMfa);
        Assert.True(policy.RequiresTicketInfo);
        Assert.True(policy.RequiresApproval);
        Assert.True(policy.RequiresAuthContext);
        Assert.Equal("c1", policy.AuthContextClaim);
    }

    [Fact]
    public async Task GetPolicyAsync_WithNoRules_AppliesSafeDefaults()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("policy-minimal.json")));
        var service = new PolicyService(GraphClientTestBuilder.Build(handler), new PolicyCache());

        var policy = await service.GetPolicyAsync(TenantA, PimResourceKind.GroupMembership, "group-x");

        Assert.Equal(TimeSpan.FromHours(8), policy.MaximumDuration);
        Assert.True(policy.RequiresJustification);
        Assert.False(policy.RequiresMfa);
        Assert.False(policy.RequiresTicketInfo);
        Assert.False(policy.RequiresApproval);
        Assert.False(policy.RequiresAuthContext);
    }

    [Fact]
    public async Task GetPolicyAsync_SecondCallForSameResource_IsServedFromCache()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("policy-directory-full.json")));
        var service = new PolicyService(GraphClientTestBuilder.Build(handler), new PolicyCache());

        await service.GetPolicyAsync(TenantA, PimResourceKind.DirectoryRole, "role-def-ga");
        await service.GetPolicyAsync(TenantA, PimResourceKind.DirectoryRole, "role-def-ga");

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetPolicyAsync_SameResourceDifferentTenants_DoesNotShareCache()
    {
        // Same role definition id in two different tenants must result in two
        // separate Graph calls — the policies are not interchangeable.
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("policy-directory-full.json")),
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("policy-directory-full.json")));
        var service = new PolicyService(GraphClientTestBuilder.Build(handler), new PolicyCache());

        await service.GetPolicyAsync(TenantA, PimResourceKind.DirectoryRole, "role-def-ga");
        await service.GetPolicyAsync(TenantB, PimResourceKind.DirectoryRole, "role-def-ga");

        Assert.Equal(2, handler.RequestCount);
    }
}
