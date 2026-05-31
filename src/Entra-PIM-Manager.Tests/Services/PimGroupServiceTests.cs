namespace EntraPimManager.Tests.Services;

using System.Net;
using EntraPimManager.Core.Models;
using EntraPimManager.Core.Services;
using EntraPimManager.Tests.TestSupport;
using Moq;

public sealed class PimGroupServiceTests
{
    [Fact]
    public async Task GetEligibleGroupAccessAsync_MapsAccessIdAndResolvesGroupNames()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("group-eligibilities.json")));

        var resolver = new Mock<IGroupResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, GroupInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["group-1"] = new GroupInfo("group-1", "grp-tier1-admins", IsAssignableToRole: true),
                ["group-2"] = new GroupInfo("group-2", "grp-project-x", IsAssignableToRole: false),
            });

        var service = new PimGroupService(GraphClientTestBuilder.Build(handler), resolver.Object);

        var result = await service.GetEligibleGroupAccessAsync();

        Assert.Equal(2, result.Count);

        var membership = result[0];
        Assert.Equal(PimResourceKind.GroupMembership, membership.Kind);
        Assert.Equal("grp-tier1-admins", membership.DisplayName);
        Assert.True(membership.IsRoleAssignableGroup);

        var ownership = result[1];
        Assert.Equal(PimResourceKind.GroupOwnership, ownership.Kind);
        Assert.Equal("grp-project-x", ownership.DisplayName);
        Assert.False(ownership.IsRoleAssignableGroup);
    }

    [Fact]
    public async Task ActivateAsync_FoldsTicketIntoJustificationAndSendsNoTicketInfo()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(
                FixtureLoader.Load("activation-group-provisioned.json"), HttpStatusCode.Created));
        var service = new PimGroupService(
            GraphClientTestBuilder.Build(handler), new Mock<IGroupResolver>().Object);

        var eligibility = new PimEligibility(PimResourceKind.GroupMembership, "grp-project-x", "group-1", "group-1", "user-oid-1", null, false);
        var request = new ActivationRequest(eligibility, TimeSpan.FromHours(3), "Project work", new TicketInfo("CHG-77", "Jira"));

        var result = await service.ActivateAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(ActivationStatus.Provisioned, result.Status);

        var body = handler.RequestBodies[0];
        Assert.NotNull(body);
        Assert.Contains("selfActivate", body, StringComparison.Ordinal);

        // PIM-for-Groups has no ticketInfo field — the ticket is folded into the justification.
        Assert.DoesNotContain("ticketInfo", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CHG-77", body, StringComparison.Ordinal);
        Assert.Contains("Project work", body, StringComparison.Ordinal);
    }
}
