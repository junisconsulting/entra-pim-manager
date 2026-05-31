namespace EntraPimManager.Tests.Services;

using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Models;
using EntraPimManager.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

public sealed class EligibilityAggregatorTests
{
    [Theory]
    [InlineData(PimResourceKind.DirectoryRole)]
    [InlineData(PimResourceKind.GroupMembership)]
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
        roleService
            .Setup(s => s.ActivateAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        groupService
            .Setup(s => s.ActivateAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var bundle = new AccountScopedServiceBundle(
            roleService.Object, groupService.Object, Mock.Of<IPolicyService>());
        var scoped = new Mock<IAccountScopedServices>();
        scoped.Setup(s => s.GetServicesFor(account)).Returns(bundle);

        var aggregator = new EligibilityAggregator(scoped.Object, NullLogger<EligibilityAggregator>.Instance);

        var result = await aggregator.ActivateAsync(account, request);

        Assert.Same(expected, result);
        var isRole = kind == PimResourceKind.DirectoryRole;
        roleService.Verify(
            s => s.ActivateAsync(request, It.IsAny<CancellationToken>()),
            isRole ? Times.Once() : Times.Never());
        groupService.Verify(
            s => s.ActivateAsync(request, It.IsAny<CancellationToken>()),
            isRole ? Times.Never() : Times.Once());
    }

    [Theory]
    [InlineData(PimResourceKind.DirectoryRole)]
    [InlineData(PimResourceKind.GroupMembership)]
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
        roleService
            .Setup(s => s.DeactivateAsync(assignment, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        groupService
            .Setup(s => s.DeactivateAsync(assignment, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var bundle = new AccountScopedServiceBundle(
            roleService.Object, groupService.Object, Mock.Of<IPolicyService>());
        var scoped = new Mock<IAccountScopedServices>();
        scoped.Setup(s => s.GetServicesFor(account)).Returns(bundle);

        var aggregator = new EligibilityAggregator(scoped.Object, NullLogger<EligibilityAggregator>.Instance);

        var result = await aggregator.DeactivateAsync(account, assignment);

        Assert.Same(expected, result);
        var isRole = kind == PimResourceKind.DirectoryRole;
        roleService.Verify(
            s => s.DeactivateAsync(assignment, It.IsAny<CancellationToken>()),
            isRole ? Times.Once() : Times.Never());
        groupService.Verify(
            s => s.DeactivateAsync(assignment, It.IsAny<CancellationToken>()),
            isRole ? Times.Never() : Times.Once());
    }

    [Fact]
    public async Task GetAggregatedActiveAssignmentsAsync_FansOutAndIsolatesFailures()
    {
        // One healthy tenant, one throwing — failure must not poison the dict.
        var goodAccount = MakeAccount("oid-good", "tenant-good");
        var badAccount = MakeAccount("oid-bad", "tenant-bad");

        var assignment = new ActiveAssignment(
            Kind: PimResourceKind.DirectoryRole,
            DisplayName: "Reader",
            ResourceId: "role-reader",
            ScopeId: "/",
            PrincipalId: "oid-good",
            StartDateTime: null,
            EndDateTime: null,
            AssignmentScheduleId: "assign-r");

        var goodRole = new Mock<IPimRoleService>();
        goodRole
            .Setup(s => s.GetActiveRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { assignment });
        var goodGroup = new Mock<IPimGroupService>();
        goodGroup
            .Setup(s => s.GetActiveGroupAccessAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ActiveAssignment>());
        var goodBundle = new AccountScopedServiceBundle(
            goodRole.Object, goodGroup.Object, Mock.Of<IPolicyService>());

        var badRole = new Mock<IPimRoleService>();
        badRole
            .Setup(s => s.GetActiveRolesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("upstream blew up"));
        var badBundle = new AccountScopedServiceBundle(
            badRole.Object, Mock.Of<IPimGroupService>(), Mock.Of<IPolicyService>());

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
        // One healthy tenant returns rows, the other throws. The failure must
        // not leak — the dict must still contain both accounts, the failed one
        // with an empty list.
        var goodAccount = MakeAccount("oid-good", "tenant-good");
        var badAccount = MakeAccount("oid-bad", "tenant-bad");

        var roleEligibility = new PimEligibility(
            Kind: PimResourceKind.DirectoryRole,
            DisplayName: "Reader",
            ResourceId: "role-reader",
            ScopeId: "/",
            PrincipalId: "oid-good",
            EndDateTime: null,
            IsRoleAssignableGroup: false);
        var groupEligibility = new PimEligibility(
            Kind: PimResourceKind.GroupMembership,
            DisplayName: "grp-tier1",
            ResourceId: "group-1",
            ScopeId: "group-1",
            PrincipalId: "oid-good",
            EndDateTime: null,
            IsRoleAssignableGroup: false);

        var goodRole = new Mock<IPimRoleService>();
        goodRole
            .Setup(s => s.GetEligibleRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { roleEligibility });
        var goodGroup = new Mock<IPimGroupService>();
        goodGroup
            .Setup(s => s.GetEligibleGroupAccessAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { groupEligibility });
        var goodBundle = new AccountScopedServiceBundle(
            goodRole.Object, goodGroup.Object, Mock.Of<IPolicyService>());

        var badRole = new Mock<IPimRoleService>();
        badRole
            .Setup(s => s.GetEligibleRolesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("upstream blew up"));
        var badBundle = new AccountScopedServiceBundle(
            badRole.Object, Mock.Of<IPimGroupService>(), Mock.Of<IPolicyService>());

        var scoped = new Mock<IAccountScopedServices>();
        scoped.Setup(s => s.GetServicesFor(goodAccount)).Returns(goodBundle);
        scoped.Setup(s => s.GetServicesFor(badAccount)).Returns(badBundle);

        var aggregator = new EligibilityAggregator(scoped.Object, NullLogger<EligibilityAggregator>.Instance);

        var result = await aggregator.GetAggregatedEligibilitiesAsync(new[] { goodAccount, badAccount });

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[goodAccount].Count);
        Assert.Contains(result[goodAccount], e => e.Kind == PimResourceKind.DirectoryRole);
        Assert.Contains(result[goodAccount], e => e.Kind == PimResourceKind.GroupMembership);
        Assert.Empty(result[badAccount]);
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

    private static SignedInAccount MakeAccount(string oid, string tenant) => new(
        ObjectId: oid,
        TenantId: tenant,
        Username: $"{oid}@example.com",
        DisplayName: $"User {oid}",
        AddedAt: DateTimeOffset.UtcNow);
}
