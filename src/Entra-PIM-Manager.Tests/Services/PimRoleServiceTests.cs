namespace EntraPimManager.Tests.Services;

using System.Net;
using EntraPimManager.Core.Models;
using EntraPimManager.Core.Services;
using EntraPimManager.Tests.TestSupport;

public sealed class PimRoleServiceTests
{
    [Fact]
    public async Task GetEligibleRolesAsync_MapsDirectoryRoleEligibilities()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("directory-eligibilities.json")));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler));

        var result = await service.GetEligibleRolesAsync();

        Assert.Equal(2, result.Count);

        var globalAdmin = result[0];
        Assert.Equal(PimResourceKind.DirectoryRole, globalAdmin.Kind);
        Assert.Equal("Global Administrator", globalAdmin.DisplayName);
        Assert.Equal("role-def-ga", globalAdmin.ResourceId);
        Assert.Equal("/", globalAdmin.ScopeId);
        Assert.False(globalAdmin.IsRoleAssignableGroup);

        // directoryScopeId must be passed through verbatim — not normalized to "/".
        Assert.Equal("/administrativeUnits/au-1", result[1].ScopeId);
    }

    [Fact]
    public async Task GetActiveRolesAsync_MapsActivatedAssignments()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("directory-active-assignments.json")));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler));

        var result = await service.GetActiveRolesAsync();

        var assignment = Assert.Single(result);
        Assert.Equal(PimResourceKind.DirectoryRole, assignment.Kind);
        Assert.Equal("Global Administrator", assignment.DisplayName);
        Assert.Equal("assign-ga", assignment.AssignmentScheduleId);
        Assert.NotNull(assignment.EndDateTime);
    }

    [Fact]
    public async Task ActivateAsync_BuildsSelfActivateRequestWithVerbatimScopeAndTicket()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(
                FixtureLoader.Load("activation-provisioned.json"), HttpStatusCode.Created));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler));

        var eligibility = new PimEligibility(PimResourceKind.DirectoryRole, "User Administrator", "role-def-ua", "/administrativeUnits/au-1", "user-oid-1", null, false);
        var request = new ActivationRequest(eligibility, TimeSpan.FromHours(2), "Fixing a locked account", new TicketInfo("INC-555", "ServiceNow"));

        var result = await service.ActivateAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(ActivationStatus.Provisioned, result.Status);

        var body = handler.RequestBodies[0];
        Assert.NotNull(body);
        Assert.Contains("selfActivate", body, StringComparison.Ordinal);
        Assert.Contains("/administrativeUnits/au-1", body, StringComparison.Ordinal);
        Assert.Contains("INC-555", body, StringComparison.Ordinal);
        Assert.Contains("Fixing a locked account", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActivateAsync_PendingApprovalResponse_IsParsedFromBody()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(
                FixtureLoader.Load("activation-pending-approval.json"), HttpStatusCode.Created));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler));

        var result = await service.ActivateAsync(SampleRequest());

        // A 201 can still carry a non-immediate status — parsed from the body.
        Assert.Equal(ActivationStatus.PendingApproval, result.Status);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ActivateAsync_PolicyViolation_ReturnsMappedValidationError()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(
                FixtureLoader.Load("error-justification-required.json"), HttpStatusCode.BadRequest));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler));

        var result = await service.ActivateAsync(SampleRequest());

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorSeverity.Validation, result.Error.Severity);
        Assert.Equal("justification", result.Error.FieldHint);
    }

    [Fact]
    public async Task ActivateAsync_StartTimeInPast_RetriesOnceThenSucceeds()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(
                FixtureLoader.Load("error-start-time-in-past.json"), HttpStatusCode.BadRequest),
            FakeHttpMessageHandler.JsonResponse(
                FixtureLoader.Load("activation-provisioned.json"), HttpStatusCode.Created));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler));

        var result = await service.ActivateAsync(SampleRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task ActivateAsync_WithIsValidationOnly_SendsFlagInPostBody()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(
                FixtureLoader.Load("activation-provisioned.json"), HttpStatusCode.Created));
        var service = new PimRoleService(GraphClientTestBuilder.Build(handler));

        var eligibility = new PimEligibility(
            PimResourceKind.DirectoryRole, "Global Administrator", "role-def-ga", "/", "user-oid-1", null, false);
        var request = new ActivationRequest(
            eligibility, TimeSpan.FromHours(1), "Dry run", null, IsValidationOnly: true);

        await service.ActivateAsync(request);

        var body = handler.RequestBodies[0];
        Assert.NotNull(body);
        Assert.Contains("\"isValidationOnly\":true", body, StringComparison.Ordinal);
    }

    private static ActivationRequest SampleRequest()
    {
        var eligibility = new PimEligibility(PimResourceKind.DirectoryRole, "Global Administrator", "role-def-ga", "/", "user-oid-1", null, false);
        return new ActivationRequest(eligibility, TimeSpan.FromHours(1), "Test justification", null);
    }
}
