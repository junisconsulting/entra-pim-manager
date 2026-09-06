namespace EntraPimManager.Tests.Services;

using EntraPimManager.Core.Arm;
using EntraPimManager.Core.Caching;
using EntraPimManager.Core.Models;
using EntraPimManager.Core.Services;
using EntraPimManager.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

public sealed class PolicyServiceTests
{
    private const string TenantA = "11111111-1111-1111-1111-111111111111";
    private const string TenantB = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task GetPolicyAsync_ParsesEndUserRulesAndIgnoresAdminRules()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("policy-directory-full.json")));
        var service = new PolicyService(GraphClientTestBuilder.Build(handler), Mock.Of<IPimAzureResourceService>(), new PolicyCache(), NullLogger<PolicyService>.Instance);

        var policy = await service.GetPolicyAsync(TenantA, PimResourceKind.DirectoryRole, "role-def-ga", "/");

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
        var service = new PolicyService(GraphClientTestBuilder.Build(handler), Mock.Of<IPimAzureResourceService>(), new PolicyCache(), NullLogger<PolicyService>.Instance);

        var policy = await service.GetPolicyAsync(TenantA, PimResourceKind.GroupMembership, "group-x", "group-x");

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
        var service = new PolicyService(GraphClientTestBuilder.Build(handler), Mock.Of<IPimAzureResourceService>(), new PolicyCache(), NullLogger<PolicyService>.Instance);

        await service.GetPolicyAsync(TenantA, PimResourceKind.DirectoryRole, "role-def-ga", "/");
        await service.GetPolicyAsync(TenantA, PimResourceKind.DirectoryRole, "role-def-ga", "/");

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
        var service = new PolicyService(GraphClientTestBuilder.Build(handler), Mock.Of<IPimAzureResourceService>(), new PolicyCache(), NullLogger<PolicyService>.Instance);

        await service.GetPolicyAsync(TenantA, PimResourceKind.DirectoryRole, "role-def-ga", "/");
        await service.GetPolicyAsync(TenantB, PimResourceKind.DirectoryRole, "role-def-ga", "/");

        Assert.Equal(2, handler.RequestCount);
    }

    [Theory]
    [InlineData(PimResourceKind.GroupMembership, "member")]
    [InlineData(PimResourceKind.GroupOwnership, "owner")]
    public async Task GetPolicyAsync_ForAGroup_FiltersOnTheMemberOrOwnerRole(
        PimResourceKind kind,
        string expectedRoleDefinitionId)
    {
        // A group has one policy for 'member' and an independent one for 'owner'
        // at the same scope. Without the roleDefinitionId filter Graph returns
        // both and the wrong one can win.
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("policy-minimal.json")));
        var service = new PolicyService(
            GraphClientTestBuilder.Build(handler), Mock.Of<IPimAzureResourceService>(), new PolicyCache(), NullLogger<PolicyService>.Instance);

        await service.GetPolicyAsync(TenantA, kind, "group-x", "group-x");

        var query = Uri.UnescapeDataString(handler.Requests[0].RequestUri!.Query);
        Assert.Contains("scopeId eq 'group-x' and scopeType eq 'Group'", query, StringComparison.Ordinal);
        Assert.Contains($"roleDefinitionId eq '{expectedRoleDefinitionId}'", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPolicyAsync_ForAnAzureResourceRole_ReadsThroughArmAtTheScope()
    {
        var expected = new ActivationPolicy { MaximumDuration = TimeSpan.FromHours(2) };
        var arm = new Mock<IPimAzureResourceService>();
        arm
            .Setup(a => a.GetPolicyAsync("/subscriptions/sub-1", "role-def-contributor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var handler = new FakeHttpMessageHandler(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
        var service = new PolicyService(
            GraphClientTestBuilder.Build(handler), arm.Object, new PolicyCache(), NullLogger<PolicyService>.Instance);

        var policy = await service.GetPolicyAsync(
            TenantA, PimResourceKind.AzureResourceRole, "role-def-contributor", "/subscriptions/sub-1");

        Assert.Same(expected, policy);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task GetPolicyAsync_SameAzureRoleAtTwoScopes_IsCachedPerScope()
    {
        // Contributor on the subscription and Contributor on one of its resource
        // groups are different policies — a role-only cache key would serve the
        // first one for both.
        var arm = new Mock<IPimAzureResourceService>();
        arm
            .Setup(a => a.GetPolicyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActivationPolicy());
        var service = new PolicyService(
            GraphClientTestBuilder.Build(new FakeHttpMessageHandler(new HttpResponseMessage())),
            arm.Object,
            new PolicyCache(),
            NullLogger<PolicyService>.Instance);

        await service.GetPolicyAsync(TenantA, PimResourceKind.AzureResourceRole, "role-def-contributor", "/subscriptions/sub-1");
        await service.GetPolicyAsync(TenantA, PimResourceKind.AzureResourceRole, "role-def-contributor", "/subscriptions/sub-1");
        await service.GetPolicyAsync(TenantA, PimResourceKind.AzureResourceRole, "role-def-contributor", "/subscriptions/sub-1/resourceGroups/rg-1");

        arm.Verify(
            a => a.GetPolicyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task GetPolicyAsync_WhenArmRejectsTheRead_FallsBackToDefaults()
    {
        // An eligible-only user may not be allowed to read the policy at all;
        // the form still opens and ARM enforces the real rules on activation.
        var arm = new Mock<IPimAzureResourceService>();
        arm
            .Setup(a => a.GetPolicyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArmRequestException(403, "AuthorizationFailed", "The client does not have authorization"));
        var service = new PolicyService(
            GraphClientTestBuilder.Build(new FakeHttpMessageHandler(new HttpResponseMessage())),
            arm.Object,
            new PolicyCache(),
            NullLogger<PolicyService>.Instance);

        var policy = await service.GetPolicyAsync(
            TenantA, PimResourceKind.AzureResourceRole, "role-def-contributor", "/subscriptions/sub-1");

        Assert.Equal(TimeSpan.FromHours(8), policy.MaximumDuration);
        Assert.True(policy.RequiresJustification);
    }

    [Fact]
    public async Task GetPolicyAsync_WhenGraphRejectsTheRead_FallsBackToDefaults()
    {
        // A tenant that never consented to RoleManagementPolicy.Read.AzureADGroup
        // answers 403 PermissionScopeNotGranted. The activation form must still
        // open — PIM enforces the real rules on the request itself.
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(
                """{"error":{"code":"PermissionScopeNotGranted","message":"Authorization failed."}}""",
                System.Net.HttpStatusCode.Forbidden));
        var service = new PolicyService(
            GraphClientTestBuilder.Build(handler), Mock.Of<IPimAzureResourceService>(), new PolicyCache(), NullLogger<PolicyService>.Instance);

        var policy = await service.GetPolicyAsync(TenantA, PimResourceKind.GroupMembership, "group-x", "group-x");

        Assert.Equal(TimeSpan.FromHours(8), policy.MaximumDuration);
        Assert.True(policy.RequiresJustification);
    }
}
