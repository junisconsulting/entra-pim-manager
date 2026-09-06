namespace EntraPimManager.Tests.Services;

using System.Net;
using EntraPimManager.Core.Arm;
using EntraPimManager.Core.Models;
using EntraPimManager.Core.Services;
using EntraPimManager.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class PimAzureResourceServiceTests
{
    private const string UserOid = "user-oid-1";
    private const string ContributorId = "/subscriptions/sub-1/providers/Microsoft.Authorization/roleDefinitions/b24988ac-6180-42a0-ab88-20f7382dd24c";

    [Fact]
    public async Task GetEligibleAzureRolesAsync_ListsAtTenantRootAndMapsScopeRoleAndLabel()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("arm-eligibilities.json")));
        var service = Build(handler);

        var result = await service.GetEligibleAzureRolesAsync();

        Assert.Equal(
            "https://management.azure.com/providers/Microsoft.Authorization/roleEligibilityScheduleInstances?api-version=2020-10-01&$filter=asTarget()",
            handler.Requests[0].RequestUri!.ToString());
        Assert.Equal(2, result.Count);

        var contributor = result[0];
        Assert.Equal(PimResourceKind.AzureResourceRole, contributor.Kind);
        Assert.Equal("Contributor", contributor.DisplayName);
        Assert.Equal(ContributorId, contributor.ResourceId);
        Assert.Equal("/subscriptions/sub-1", contributor.ScopeId);
        Assert.Equal("Subscription: Pay-As-You-Go", contributor.ScopeLabel);
        Assert.Equal(UserOid, contributor.PrincipalId);
        Assert.NotNull(contributor.EndDateTime);
        Assert.False(contributor.IsRoleAssignableGroup);

        // Inherited through a group: ARM reports the group as principal, but
        // the activation must be requested for the signed-in user.
        var reader = result[1];
        Assert.Equal("Reader", reader.DisplayName);
        Assert.Equal("/subscriptions/sub-1/resourceGroups/rg-prod", reader.ScopeId);
        Assert.Equal("Resource group: rg-prod", reader.ScopeLabel);
        Assert.Equal(UserOid, reader.PrincipalId);
        Assert.Null(reader.EndDateTime);
    }

    [Fact]
    public async Task GetEligibleAzureRolesAsync_FollowsNextLink()
    {
        const string nextLink = "https://management.azure.com/providers/Microsoft.Authorization/roleEligibilityScheduleInstances?api-version=2020-10-01&$skipToken=page2";
        var firstPage = FixtureLoader.Load("arm-eligibilities.json")
            .Replace("\"value\": [", $"\"nextLink\": \"{nextLink}\", \"value\": [", StringComparison.Ordinal);
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(firstPage),
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("arm-eligibilities-page2.json")));
        var service = Build(handler);

        var result = await service.GetEligibleAzureRolesAsync();

        Assert.Equal(3, result.Count);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(nextLink, handler.Requests[1].RequestUri!.ToString());
        Assert.Equal("Management group: MG-1", result[2].ScopeLabel);
    }

    [Fact]
    public async Task GetActiveAzureRolesAsync_KeepsOnlyActivatedRows()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("arm-active-assignments.json")));
        var service = Build(handler);

        var result = await service.GetActiveAzureRolesAsync();

        Assert.Contains("roleAssignmentScheduleInstances?api-version=2020-10-01&$filter=asTarget()", handler.Requests[0].RequestUri!.ToString(), StringComparison.Ordinal);
        var assignment = Assert.Single(result);
        Assert.Equal(PimResourceKind.AzureResourceRole, assignment.Kind);
        Assert.Equal("Contributor", assignment.DisplayName);
        Assert.Equal("arm-assign-1", assignment.AssignmentScheduleId);
        Assert.Equal("/subscriptions/sub-1", assignment.ScopeId);
        Assert.Equal("Subscription: Pay-As-You-Go", assignment.ScopeLabel);
        Assert.Equal(UserOid, assignment.PrincipalId);
        Assert.NotNull(assignment.StartDateTime);
        Assert.NotNull(assignment.EndDateTime);
    }

    [Fact]
    public async Task ActivateAsync_PutsSelfActivateUnderAClientGuidAtTheEligibilityScope()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("arm-activation-provisioned.json"), HttpStatusCode.Created));
        var service = Build(handler);
        var request = new ActivationRequest(
            SampleEligibility(), TimeSpan.FromHours(2), "Fixing a locked account", new TicketInfo("INC-555", "ServiceNow"));

        var result = await service.ActivateAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(ActivationStatus.Provisioned, result.Status);
        Assert.True(Guid.TryParse(result.RequestId, out _));
        Assert.Equal(DateTimeOffset.Parse("2026-09-04T10:00:00Z", null), result.EndDateTime);

        var sent = handler.Requests[0];
        Assert.Equal(HttpMethod.Put, sent.Method);
        Assert.StartsWith(
            $"https://management.azure.com/subscriptions/sub-1/providers/Microsoft.Authorization/roleAssignmentScheduleRequests/{result.RequestId}?api-version=2020-10-01",
            sent.RequestUri!.ToString(),
            StringComparison.Ordinal);

        var body = handler.RequestBodies[0];
        Assert.NotNull(body);
        Assert.Contains("\"requestType\":\"SelfActivate\"", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"AfterDuration\"", body, StringComparison.Ordinal);
        Assert.Contains("\"duration\":\"PT2H\"", body, StringComparison.Ordinal);
        Assert.Contains($"\"principalId\":\"{UserOid}\"", body, StringComparison.Ordinal);
        Assert.Contains("INC-555", body, StringComparison.Ordinal);
        Assert.Contains("Fixing a locked account", body, StringComparison.Ordinal);
        Assert.DoesNotContain("startDateTime", body, StringComparison.Ordinal);
        Assert.DoesNotContain("isValidationOnly", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActivateAsync_PendingProvisioningResponse_CountsAsAccepted()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("arm-activation-pending-provisioning.json"), HttpStatusCode.Created));
        var service = Build(handler);

        var result = await service.ActivateAsync(SampleRequest());

        // ARM accepted the request but has not provisioned it yet — the UI
        // treats it like a pending schedule creation and refreshes.
        Assert.Equal(ActivationStatus.PendingScheduleCreation, result.Status);
        Assert.True(result.IsSuccess);
        Assert.Equal(DateTimeOffset.Parse("2026-09-04T09:00:00Z", null), result.EndDateTime);
    }

    [Fact]
    public async Task ActivateAsync_WithAuthContextClaim_StampsClaimsOnTheRequest()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("arm-activation-provisioned.json"), HttpStatusCode.Created));
        var service = Build(handler);

        await service.ActivateAsync(SampleRequest() with { AuthContextClaim = "c1" });

        Assert.True(handler.Requests[0].Options.TryGetValue(ArmBearerTokenHandler.ClaimsOption, out var claims));
        Assert.Contains("\"acrs\"", claims, StringComparison.Ordinal);
        Assert.Contains("\"c1\"", claims, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActivateAsync_WithoutAuthContextClaim_StampsNoClaims()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("arm-activation-provisioned.json"), HttpStatusCode.Created));
        var service = Build(handler);

        await service.ActivateAsync(SampleRequest());

        Assert.False(handler.Requests[0].Options.TryGetValue(ArmBearerTokenHandler.ClaimsOption, out _));
    }

    [Fact]
    public async Task ActivateAsync_ValidationOnly_IsRefusedWithoutARequest()
    {
        // ARM has no dry-run mode; a fake "pre-check successful" would claim
        // something nobody checked.
        var handler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var service = Build(handler);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => service.ActivateAsync(SampleRequest() with { IsValidationOnly = true }));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ActivateAsync_PolicyViolation_ReturnsMappedError()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("arm-error-policy-mfa.json"), HttpStatusCode.BadRequest));
        var service = Build(handler);

        var result = await service.ActivateAsync(SampleRequest());

        Assert.False(result.IsSuccess);
        Assert.Equal(ActivationStatus.Failed, result.Status);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorSeverity.StepUpRequired, result.Error.Severity);
    }

    [Fact]
    public async Task ActivateAsync_NonJsonErrorBody_StillFailsGracefully()
    {
        var handler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>proxy error</html>"),
        });
        var service = Build(handler);

        var result = await service.ActivateAsync(SampleRequest());

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorSeverity.Fatal, result.Error.Severity);
    }

    [Fact]
    public async Task ActivateAsync_WrongKind_Throws()
    {
        var service = Build(new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)));
        var eligibility = new PimEligibility(PimResourceKind.DirectoryRole, "Global Administrator", "role-def-ga", "/", UserOid, null, false);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ActivateAsync(new ActivationRequest(eligibility, TimeSpan.FromHours(1), null, null)));
    }

    [Fact]
    public async Task DeactivateAsync_PutsSelfDeactivateAtTheAssignmentScope()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("arm-activation-provisioned.json"), HttpStatusCode.Created));
        var service = Build(handler);
        var assignment = new ActiveAssignment(
            PimResourceKind.AzureResourceRole, "Reader", ContributorId, "/subscriptions/sub-1/resourceGroups/rg-prod", UserOid, null, null, "arm-assign-1");

        var result = await service.DeactivateAsync(assignment);

        Assert.Null(result.Error);
        var sent = handler.Requests[0];
        Assert.Equal(HttpMethod.Put, sent.Method);
        Assert.StartsWith(
            "https://management.azure.com/subscriptions/sub-1/resourceGroups/rg-prod/providers/Microsoft.Authorization/roleAssignmentScheduleRequests/",
            sent.RequestUri!.ToString(),
            StringComparison.Ordinal);
        var body = handler.RequestBodies[0];
        Assert.NotNull(body);
        Assert.Contains("\"requestType\":\"SelfDeactivate\"", body, StringComparison.Ordinal);
        Assert.Contains($"\"principalId\":\"{UserOid}\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("scheduleInfo", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPolicyAsync_FiltersByRoleAndParsesTheEndUserRules()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("arm-policy-full.json")));
        var service = Build(handler);

        var policy = await service.GetPolicyAsync("/subscriptions/sub-1", ContributorId);

        var query = Uri.UnescapeDataString(handler.Requests[0].RequestUri!.Query);
        Assert.Contains("/subscriptions/sub-1/providers/Microsoft.Authorization/roleManagementPolicyAssignments", handler.Requests[0].RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains($"$filter=roleDefinitionId eq '{ContributorId}'", query, StringComparison.Ordinal);

        // The fixture lists another role's policy first and an admin-level
        // expiration rule on the wanted one: neither may leak into the result.
        Assert.Equal(TimeSpan.FromHours(4), policy.MaximumDuration);
        Assert.True(policy.RequiresJustification);
        Assert.True(policy.RequiresTicketInfo);
        Assert.True(policy.RequiresMfa);
        Assert.True(policy.RequiresApproval);
        Assert.True(policy.RequiresAuthContext);
        Assert.Equal("c1", policy.AuthContextClaim);
    }

    [Fact]
    public async Task GetPolicyAsync_NoMatchingAssignment_AppliesSafeDefaults()
    {
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.JsonResponse("""{"value":[]}"""));
        var service = Build(handler);

        var policy = await service.GetPolicyAsync("/subscriptions/sub-1", ContributorId);

        Assert.Equal(TimeSpan.FromHours(8), policy.MaximumDuration);
        Assert.True(policy.RequiresJustification);
        Assert.False(policy.RequiresAuthContext);
    }

    [Fact]
    public async Task GetPolicyAsync_WhenArmRejectsTheRead_ThrowsWithTheArmCode()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("arm-error-authorization-failed.json"), HttpStatusCode.Forbidden));
        var service = Build(handler);

        var error = await Assert.ThrowsAsync<ArmRequestException>(
            () => service.GetPolicyAsync("/subscriptions/sub-1", ContributorId));

        Assert.Equal(403, error.StatusCode);
        Assert.Equal("AuthorizationFailed", error.Code);
    }

    [Fact]
    public async Task ActivateAsync_WithoutATicketSystem_OmitsTheFieldInsteadOfSendingItEmpty()
    {
        // The ticketing rule requires a number, not a system — an empty string is
        // not the same as "not given" to ARM, so the property must disappear.
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("arm-activation-provisioned.json"), HttpStatusCode.Created));
        var service = Build(handler);
        var request = new ActivationRequest(
            SampleEligibility(), TimeSpan.FromHours(1), "Fixing a locked account", new TicketInfo("INC-555", null));

        var result = await service.ActivateAsync(request);

        Assert.True(result.IsSuccess);
        var body = handler.RequestBodies[0];
        Assert.NotNull(body);
        Assert.Contains("INC-555", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ticketSystem", body, StringComparison.OrdinalIgnoreCase);
    }

    private static PimAzureResourceService Build(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://management.azure.com/") },
        UserOid,
        NullLogger<PimAzureResourceService>.Instance);

    private static PimEligibility SampleEligibility() => new(
        PimResourceKind.AzureResourceRole,
        "Contributor",
        ContributorId,
        "/subscriptions/sub-1",
        UserOid,
        null,
        false,
        "Subscription: Pay-As-You-Go");

    private static ActivationRequest SampleRequest() =>
        new(SampleEligibility(), TimeSpan.FromHours(1), "Test justification", null);
}
