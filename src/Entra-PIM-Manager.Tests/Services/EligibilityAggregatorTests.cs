namespace EntraPimManager.Tests.Services;

using EntraPimManager.Core.Arm;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Models;
using EntraPimManager.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;
using Moq;

public sealed class EligibilityAggregatorTests
{
    [Theory]
    [InlineData(PimResourceKind.DirectoryRole)]
    [InlineData(PimResourceKind.GroupMembership)]
    [InlineData(PimResourceKind.AzureResourceRole)]
    public async Task ActivateAsync_RoutesByResourceKind(PimResourceKind kind)
    {
        var account = MakeAccount("oid-a", "tenant-a");
        var eligibility = new PimEligibility(
            Kind: kind,
            DisplayName: "Resource",
            ResourceId: "resource-1",
            ScopeId: "scope-1",
            PrincipalId: "user-oid-1",
            EndDateTime: null,
            IsRoleAssignableGroup: false);
        var request = new ActivationRequest(eligibility, TimeSpan.FromHours(1), "Justification", null);
        var expected = new ActivationResult("req-1", ActivationStatus.Provisioned, null, null, null);

        var roleService = new Mock<IPimRoleService>();
        var groupService = new Mock<IPimGroupService>();
        var azureService = new Mock<IPimAzureResourceService>();
        roleService
            .Setup(s => s.ActivateAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        groupService
            .Setup(s => s.ActivateAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        azureService
            .Setup(s => s.ActivateAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var bundle = new AccountScopedServiceBundle(
            roleService.Object, groupService.Object, azureService.Object, Mock.Of<IPolicyService>());
        var scoped = new Mock<IAccountScopedServices>();
        scoped.Setup(s => s.GetServicesFor(account)).Returns(bundle);

        var aggregator = new EligibilityAggregator(scoped.Object, NullLogger<EligibilityAggregator>.Instance);

        var result = await aggregator.ActivateAsync(account, request);

        Assert.Same(expected, result);
        roleService.Verify(
            s => s.ActivateAsync(request, It.IsAny<CancellationToken>()),
            kind == PimResourceKind.DirectoryRole ? Times.Once() : Times.Never());
        groupService.Verify(
            s => s.ActivateAsync(request, It.IsAny<CancellationToken>()),
            kind == PimResourceKind.GroupMembership ? Times.Once() : Times.Never());
        azureService.Verify(
            s => s.ActivateAsync(request, It.IsAny<CancellationToken>()),
            kind == PimResourceKind.AzureResourceRole ? Times.Once() : Times.Never());
    }

    [Theory]
    [InlineData(PimResourceKind.DirectoryRole)]
    [InlineData(PimResourceKind.GroupMembership)]
    [InlineData(PimResourceKind.AzureResourceRole)]
    public async Task DeactivateAsync_RoutesByResourceKind(PimResourceKind kind)
    {
        var account = MakeAccount("oid-a", "tenant-a");
        var assignment = new ActiveAssignment(
            Kind: kind,
            DisplayName: "Resource",
            ResourceId: "resource-1",
            ScopeId: "scope-1",
            PrincipalId: "user-oid-1",
            StartDateTime: null,
            EndDateTime: null,
            AssignmentScheduleId: "assign-1");
        var expected = new ActivationResult("req-1", ActivationStatus.Revoked, null, null, null);

        var roleService = new Mock<IPimRoleService>();
        var groupService = new Mock<IPimGroupService>();
        var azureService = new Mock<IPimAzureResourceService>();
        roleService
            .Setup(s => s.DeactivateAsync(assignment, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        groupService
            .Setup(s => s.DeactivateAsync(assignment, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        azureService
            .Setup(s => s.DeactivateAsync(assignment, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var bundle = new AccountScopedServiceBundle(
            roleService.Object, groupService.Object, azureService.Object, Mock.Of<IPolicyService>());
        var scoped = new Mock<IAccountScopedServices>();
        scoped.Setup(s => s.GetServicesFor(account)).Returns(bundle);

        var aggregator = new EligibilityAggregator(scoped.Object, NullLogger<EligibilityAggregator>.Instance);

        var result = await aggregator.DeactivateAsync(account, assignment);

        Assert.Same(expected, result);
        roleService.Verify(
            s => s.DeactivateAsync(assignment, It.IsAny<CancellationToken>()),
            kind == PimResourceKind.DirectoryRole ? Times.Once() : Times.Never());
        groupService.Verify(
            s => s.DeactivateAsync(assignment, It.IsAny<CancellationToken>()),
            kind == PimResourceKind.GroupMembership ? Times.Once() : Times.Never());
        azureService.Verify(
            s => s.DeactivateAsync(assignment, It.IsAny<CancellationToken>()),
            kind == PimResourceKind.AzureResourceRole ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task GetAggregatedActiveAssignmentsAsync_FansOutAndIsolatesFailures()
    {
        // One healthy tenant, one throwing — failure must not poison the dict.
        var goodAccount = MakeAccount("oid-good", "tenant-good");
        var badAccount = MakeAccount("oid-bad", "tenant-bad");

        var goodRole = new Mock<IPimRoleService>();
        goodRole
            .Setup(s => s.GetActiveRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ActiveRole("oid-good") });
        var goodBundle = new AccountScopedServiceBundle(
            goodRole.Object, EmptyGroupService(), EmptyAzureService(), Mock.Of<IPolicyService>());

        var badRole = new Mock<IPimRoleService>();
        badRole
            .Setup(s => s.GetActiveRolesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("upstream blew up"));
        var badBundle = new AccountScopedServiceBundle(
            badRole.Object, Mock.Of<IPimGroupService>(), Mock.Of<IPimAzureResourceService>(), Mock.Of<IPolicyService>());

        var scoped = new Mock<IAccountScopedServices>();
        scoped.Setup(s => s.GetServicesFor(goodAccount)).Returns(goodBundle);
        scoped.Setup(s => s.GetServicesFor(badAccount)).Returns(badBundle);

        var aggregator = new EligibilityAggregator(scoped.Object, NullLogger<EligibilityAggregator>.Instance);

        var result = await aggregator.GetAggregatedActiveAssignmentsAsync(new[] { goodAccount, badAccount });

        Assert.Equal(2, result.Count);
        Assert.Single(result[goodAccount]);
        Assert.Empty(result[badAccount]);
    }

    [Fact]
    public async Task GetAggregatedEligibilitiesAsync_FansOutAndIsolatesFailures()
    {
        // One healthy tenant returns rows from all three surfaces, the other
        // throws. The failure must not leak — the dict must still contain both
        // accounts, the failed one with an empty list and a user-facing reason.
        var goodAccount = MakeAccount("oid-good", "tenant-good");
        var badAccount = MakeAccount("oid-bad", "tenant-bad");

        var goodRole = new Mock<IPimRoleService>();
        goodRole
            .Setup(s => s.GetEligibleRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Eligibility(PimResourceKind.DirectoryRole, "oid-good") });
        var goodGroup = new Mock<IPimGroupService>();
        goodGroup
            .Setup(s => s.GetEligibleGroupAccessAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Eligibility(PimResourceKind.GroupMembership, "oid-good") });
        var goodAzure = new Mock<IPimAzureResourceService>();
        goodAzure
            .Setup(s => s.GetEligibleAzureRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Eligibility(PimResourceKind.AzureResourceRole, "oid-good") });
        var goodBundle = new AccountScopedServiceBundle(
            goodRole.Object, goodGroup.Object, goodAzure.Object, Mock.Of<IPolicyService>());

        var badRole = new Mock<IPimRoleService>();
        badRole
            .Setup(s => s.GetEligibleRolesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("upstream blew up"));
        var badBundle = new AccountScopedServiceBundle(
            badRole.Object, Mock.Of<IPimGroupService>(), Mock.Of<IPimAzureResourceService>(), Mock.Of<IPolicyService>());

        var scoped = new Mock<IAccountScopedServices>();
        scoped.Setup(s => s.GetServicesFor(goodAccount)).Returns(goodBundle);
        scoped.Setup(s => s.GetServicesFor(badAccount)).Returns(badBundle);

        var aggregator = new EligibilityAggregator(scoped.Object, NullLogger<EligibilityAggregator>.Instance);

        var result = await aggregator.GetAggregatedEligibilitiesAsync(new[] { goodAccount, badAccount });

        Assert.Equal(2, result.Count);
        Assert.Equal(3, result[goodAccount].Items.Count);
        Assert.Contains(result[goodAccount].Items, e => e.Kind == PimResourceKind.DirectoryRole);
        Assert.Contains(result[goodAccount].Items, e => e.Kind == PimResourceKind.GroupMembership);
        Assert.Contains(result[goodAccount].Items, e => e.Kind == PimResourceKind.AzureResourceRole);
        Assert.Null(result[goodAccount].LoadError);
        Assert.Empty(result[badAccount].Items);
        Assert.NotNull(result[badAccount].LoadError);
    }

    [Fact]
    public async Task GetAggregatedEligibilitiesAsync_ArmFailure_KeepsGraphItemsAndReportsLoadError()
    {
        // A tenant without the Azure Service Management permission must still
        // show its directory roles and groups; only the Azure rows are missing,
        // and the group says why.
        var account = MakeAccount("oid-a", "tenant-a");
        var azure = new Mock<IPimAzureResourceService>();
        azure
            .Setup(s => s.GetEligibleAzureRolesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArmRequestException(403, "AuthorizationFailed", "no authorization"));
        var aggregator = BuildAggregator(account, azure.Object);

        var result = await aggregator.GetAggregatedEligibilitiesAsync(new[] { account });

        Assert.Equal(2, result[account].Items.Count);
        Assert.DoesNotContain(result[account].Items, e => e.Kind == PimResourceKind.AzureResourceRole);
        Assert.StartsWith("Azure resource roles unavailable", result[account].LoadError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAggregatedActiveAssignmentsAsync_ArmFailure_KeepsGraphItems()
    {
        var account = MakeAccount("oid-a", "tenant-a");
        var azure = new Mock<IPimAzureResourceService>();
        azure
            .Setup(s => s.GetActiveAzureRolesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArmRequestException(403, "AuthorizationFailed", "no authorization"));
        var aggregator = BuildAggregator(account, azure.Object);

        var result = await aggregator.GetAggregatedActiveAssignmentsAsync(new[] { account });

        Assert.Single(result[account]);
    }

    [Fact]
    public async Task GetAggregatedEligibilitiesAsync_ArmRequestFailure_IsRetriedOnTheNextRefresh()
    {
        // An ordinary ARM error costs one failed HTTP call per refresh — no
        // reason to back off, the tenant may consent any minute.
        var account = MakeAccount("oid-a", "tenant-a");
        var azure = new Mock<IPimAzureResourceService>();
        azure
            .Setup(s => s.GetEligibleAzureRolesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArmRequestException(503, string.Empty, string.Empty));
        var aggregator = BuildAggregator(account, azure.Object);

        await aggregator.GetAggregatedEligibilitiesAsync(new[] { account });
        await aggregator.GetAggregatedEligibilitiesAsync(new[] { account });

        azure.Verify(s => s.GetEligibleAzureRolesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetAggregatedEligibilitiesAsync_ArmTimeout_RetriesOnTheNextRefresh()
    {
        // A slow read is not a failed sign-in. Listing every scope of a large estate
        // can simply exceed the budget once, and an hour without Azure roles is a far
        // worse answer than one wasted call a minute later.
        var account = MakeAccount("oid-a", "tenant-a");
        var azure = new Mock<IPimAzureResourceService>();
        azure
            .Setup(s => s.GetEligibleAzureRolesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());
        var aggregator = BuildAggregator(account, azure.Object);

        var first = await aggregator.GetAggregatedEligibilitiesAsync(new[] { account });
        await aggregator.GetAggregatedEligibilitiesAsync(new[] { account });

        azure.Verify(s => s.GetEligibleAzureRolesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Contains("timed out", first[account].LoadError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAggregatedEligibilitiesAsync_ArmSignInPending_RetriesOnTheNextRefresh()
    {
        // A token call that didn't finish is a transient condition, not a verdict on
        // the tenant's consent — the background read never opens a prompt, so there is
        // nothing to suppress and the next tick may simply succeed.
        var account = MakeAccount("oid-a", "tenant-a");
        var azure = new Mock<IPimAzureResourceService>();
        azure
            .Setup(s => s.GetEligibleAzureRolesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArmSignInPendingException(new OperationCanceledException()));
        var aggregator = BuildAggregator(account, azure.Object);

        await aggregator.GetAggregatedEligibilitiesAsync(new[] { account });
        await aggregator.GetAggregatedEligibilitiesAsync(new[] { account });

        azure.Verify(s => s.GetEligibleAzureRolesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetAggregatedEligibilitiesAsync_MsalFailureOnArm_BacksOffAndKeepsTheReason()
    {
        // A failed token acquisition means a WAM prompt was (or would be) shown;
        // repeating that on every 60 s refresh is the failure mode the backoff
        // exists for. The reason stays visible while the surface is skipped.
        var account = MakeAccount("oid-a", "tenant-a");
        var azure = new Mock<IPimAzureResourceService>();
        azure
            .Setup(s => s.GetEligibleAzureRolesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MsalUiRequiredException("invalid_grant", "AADSTS65001: The user or administrator has not consented"));
        var aggregator = BuildAggregator(account, azure.Object);

        var first = await aggregator.GetAggregatedEligibilitiesAsync(new[] { account });
        var second = await aggregator.GetAggregatedEligibilitiesAsync(new[] { account });

        azure.Verify(s => s.GetEligibleAzureRolesAsync(It.IsAny<CancellationToken>()), Times.Once());
        Assert.Equal(2, second[account].Items.Count);
        Assert.Contains("consent", first[account].LoadError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(first[account].LoadError, second[account].LoadError);
    }

    [Fact]
    public async Task ForgetAzureBackoff_AfterConsentAndAReSignIn_TriesTheAzureSurfaceAgain()
    {
        // The backoff key is (oid, tenant, cloud), so removing the account and signing
        // in again leaves it in place — and that is exactly what someone does once an
        // admin has finally granted the Azure consent. Without this the obvious remedy
        // changes nothing and the tenant stays dark for the rest of the hour.
        var account = MakeAccount("oid-a", "tenant-a");
        var azure = new Mock<IPimAzureResourceService>();
        azure
            .Setup(s => s.GetEligibleAzureRolesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MsalUiRequiredException("invalid_grant", "AADSTS65001: not consented"));
        var aggregator = BuildAggregator(account, azure.Object);

        await aggregator.GetAggregatedEligibilitiesAsync(new[] { account });
        await aggregator.GetAggregatedEligibilitiesAsync(new[] { account });
        azure.Verify(s => s.GetEligibleAzureRolesAsync(It.IsAny<CancellationToken>()), Times.Once());

        aggregator.ForgetAzureBackoff(account);
        await aggregator.GetAggregatedEligibilitiesAsync(new[] { account });

        azure.Verify(s => s.GetEligibleAzureRolesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void ForgetAzureBackoff_AnAccountThatNeverFailed_IsHarmless()
    {
        var aggregator = new EligibilityAggregator(
            Mock.Of<IAccountScopedServices>(),
            NullLogger<EligibilityAggregator>.Instance);

        aggregator.ForgetAzureBackoff(MakeAccount("oid-a", "tenant-a"));
    }

    [Fact]
    public async Task GetAggregatedEligibilitiesAsync_EmptyInput_ReturnsEmptyDict()
    {
        var aggregator = new EligibilityAggregator(
            Mock.Of<IAccountScopedServices>(),
            NullLogger<EligibilityAggregator>.Instance);

        var result = await aggregator.GetAggregatedEligibilitiesAsync(Array.Empty<SignedInAccount>());

        Assert.Empty(result);
    }

    /// <summary>One account whose Graph surfaces return one row each and whose Azure surface is the given mock.</summary>
    private static EligibilityAggregator BuildAggregator(SignedInAccount account, IPimAzureResourceService azure)
    {
        var role = new Mock<IPimRoleService>();
        role
            .Setup(s => s.GetEligibleRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Eligibility(PimResourceKind.DirectoryRole, account.ObjectId) });
        role
            .Setup(s => s.GetActiveRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ActiveRole(account.ObjectId) });
        var group = new Mock<IPimGroupService>();
        group
            .Setup(s => s.GetEligibleGroupAccessAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Eligibility(PimResourceKind.GroupMembership, account.ObjectId) });
        group
            .Setup(s => s.GetActiveGroupAccessAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ActiveAssignment>());
        var bundle = new AccountScopedServiceBundle(role.Object, group.Object, azure, Mock.Of<IPolicyService>());
        var scoped = new Mock<IAccountScopedServices>();
        scoped.Setup(s => s.GetServicesFor(account)).Returns(bundle);
        return new EligibilityAggregator(scoped.Object, NullLogger<EligibilityAggregator>.Instance);
    }

    private static IPimGroupService EmptyGroupService()
    {
        var group = new Mock<IPimGroupService>();
        group
            .Setup(s => s.GetActiveGroupAccessAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ActiveAssignment>());
        return group.Object;
    }

    private static IPimAzureResourceService EmptyAzureService()
    {
        var azure = new Mock<IPimAzureResourceService>();
        azure
            .Setup(s => s.GetActiveAzureRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ActiveAssignment>());
        return azure.Object;
    }

    private static PimEligibility Eligibility(PimResourceKind kind, string oid) => new(
        Kind: kind,
        DisplayName: "Resource",
        ResourceId: $"resource-{kind}",
        ScopeId: "/",
        PrincipalId: oid,
        EndDateTime: null,
        IsRoleAssignableGroup: false);

    private static ActiveAssignment ActiveRole(string oid) => new(
        Kind: PimResourceKind.DirectoryRole,
        DisplayName: "Reader",
        ResourceId: "role-reader",
        ScopeId: "/",
        PrincipalId: oid,
        StartDateTime: null,
        EndDateTime: null,
        AssignmentScheduleId: "assign-r");

    private static SignedInAccount MakeAccount(string oid, string tenant) => new(
        ObjectId: oid,
        TenantId: tenant,
        Username: $"{oid}@example.com",
        DisplayName: $"User {oid}",
        AddedAt: DateTimeOffset.UtcNow);
}
