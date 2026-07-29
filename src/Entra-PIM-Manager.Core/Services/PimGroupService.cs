namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Auth;
using EntraPimManager.Core.ErrorHandling;
using EntraPimManager.Core.Graph;
using EntraPimManager.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

/// <summary>
/// Reads and activates the PIM-for-Groups surface
/// (<c>/identityGovernance/privilegedAccess/group/*</c>). Group display names are
/// resolved separately via <see cref="IGroupResolver"/> — <c>$expand=group</c> is
/// not used because it is unreliable on this surface. This surface has no
/// <c>ticketInfo</c> field, so a ticket reference is folded into the justification.
/// </summary>
public sealed class PimGroupService : IPimGroupService
{
    private readonly GraphServiceClient _graph;
    private readonly IGroupResolver _groupResolver;
    private readonly ILogger<PimGroupService> _logger;

    public PimGroupService(GraphServiceClient graph, IGroupResolver groupResolver, ILogger<PimGroupService> logger)
    {
        _graph = graph;
        _groupResolver = groupResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PimEligibility>> GetEligibleGroupAccessAsync(CancellationToken ct = default)
    {
        var response = await _graph.IdentityGovernance.PrivilegedAccess.Group.EligibilityScheduleInstances
            .FilterByCurrentUserWithOn("principal")
            .GetAsFilterByCurrentUserWithOnGetResponseAsync(cancellationToken: ct)
            .ConfigureAwait(false);

        var instances = response?.Value ?? [];
        if (instances.Count == 0)
        {
            return [];
        }

        var groups = await _groupResolver
            .ResolveAsync(instances.Select(i => i.GroupId ?? string.Empty), ct)
            .ConfigureAwait(false);

        return instances.Select(instance => ToEligibility(instance, groups)).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActiveAssignment>> GetActiveGroupAccessAsync(CancellationToken ct = default)
    {
        var response = await _graph.IdentityGovernance.PrivilegedAccess.Group.AssignmentScheduleInstances
            .FilterByCurrentUserWithOn("principal")
            .GetAsFilterByCurrentUserWithOnGetResponseAsync(
                requestConfiguration =>
                    requestConfiguration.QueryParameters.Filter = "assignmentType eq 'Activated'",
                ct)
            .ConfigureAwait(false);

        var instances = response?.Value ?? [];
        if (instances.Count == 0)
        {
            return [];
        }

        var groups = await _groupResolver
            .ResolveAsync(instances.Select(i => i.GroupId ?? string.Empty), ct)
            .ConfigureAwait(false);

        return instances.Select(instance => ToActiveAssignment(instance, groups)).ToList();
    }

    /// <inheritdoc />
    public async Task<ActivationResult> ActivateAsync(ActivationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Eligibility.Kind is not (PimResourceKind.GroupMembership or PimResourceKind.GroupOwnership))
        {
            throw new ArgumentException("Expected a group eligibility.", nameof(request));
        }

        try
        {
            return await SubmitActivationAsync(request, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        }
        catch (ODataError error) when (PimErrorMapper.IsStartTimeInPast(error))
        {
            try
            {
                // Clock skew between client and Microsoft — retry once, slightly in the future.
                return await SubmitActivationAsync(request, DateTimeOffset.UtcNow.AddSeconds(30), ct)
                    .ConfigureAwait(false);
            }
            catch (ODataError retryError)
            {
                return Failure(retryError);
            }
        }
        catch (ODataError error)
        {
            return Failure(error);
        }
    }

    /// <inheritdoc />
    public async Task<ActivationResult> DeactivateAsync(ActiveAssignment assignment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        if (assignment.Kind is not (PimResourceKind.GroupMembership or PimResourceKind.GroupOwnership))
        {
            throw new ArgumentException("Expected a group assignment.", nameof(assignment));
        }

        var body = new PrivilegedAccessGroupAssignmentScheduleRequest
        {
            Action = ScheduleRequestActions.SelfDeactivate,
            AccessId = ToAccessId(assignment.Kind),
            PrincipalId = assignment.PrincipalId,
            GroupId = assignment.ResourceId,
        };

        try
        {
            var response = await _graph.IdentityGovernance.PrivilegedAccess.Group.AssignmentScheduleRequests
                .PostAsync(body, cancellationToken: ct)
                .ConfigureAwait(false);

            return new ActivationResult(
                RequestId: response?.Id ?? string.Empty,
                Status: ActivationStatusParser.Parse(response?.Status),
                StartDateTime: null,
                EndDateTime: null,
                Error: null);
        }
        catch (ODataError error)
        {
            return Failure(error);
        }
    }

    private static PimResourceKind ToKind(PrivilegedAccessGroupRelationships? accessId) =>
        accessId == PrivilegedAccessGroupRelationships.Owner
            ? PimResourceKind.GroupOwnership
            : PimResourceKind.GroupMembership;

    private static PrivilegedAccessGroupRelationships ToAccessId(PimResourceKind kind) =>
        kind == PimResourceKind.GroupOwnership
            ? PrivilegedAccessGroupRelationships.Owner
            : PrivilegedAccessGroupRelationships.Member;

    private static string FallbackName(string groupId) => $"(group {groupId})";

    private static string? BuildJustification(ActivationRequest request)
    {
        // PIM-for-Groups has no ticketInfo field — fold any ticket reference into the text.
        if (request.Ticket is null)
        {
            return request.Justification;
        }

        var ticketText = $"[Ticket: {request.Ticket.TicketSystem} {request.Ticket.TicketNumber}]";
        return string.IsNullOrWhiteSpace(request.Justification)
            ? ticketText
            : $"{request.Justification} {ticketText}";
    }

    private static PimEligibility ToEligibility(
        PrivilegedAccessGroupEligibilityScheduleInstance instance,
        IReadOnlyDictionary<string, GroupInfo> groups)
    {
        var groupId = instance.GroupId ?? string.Empty;
        var group = groups.GetValueOrDefault(groupId);
        return new PimEligibility(
            Kind: ToKind(instance.AccessId),
            DisplayName: group?.DisplayName ?? FallbackName(groupId),
            ResourceId: groupId,
            ScopeId: groupId,
            PrincipalId: instance.PrincipalId ?? string.Empty,
            EndDateTime: instance.EndDateTime,
            IsRoleAssignableGroup: group?.IsAssignableToRole ?? false);
    }

    private static ActiveAssignment ToActiveAssignment(
        PrivilegedAccessGroupAssignmentScheduleInstance instance,
        IReadOnlyDictionary<string, GroupInfo> groups)
    {
        var groupId = instance.GroupId ?? string.Empty;
        var group = groups.GetValueOrDefault(groupId);
        return new ActiveAssignment(
            Kind: ToKind(instance.AccessId),
            DisplayName: group?.DisplayName ?? FallbackName(groupId),
            ResourceId: groupId,
            ScopeId: groupId,
            PrincipalId: instance.PrincipalId ?? string.Empty,
            StartDateTime: instance.StartDateTime,
            EndDateTime: instance.EndDateTime,
            AssignmentScheduleId: instance.Id ?? string.Empty);
    }

    private ActivationResult Failure(ODataError error)
    {
        _logger.LogWarning(
            "PIM group request failed: {Code} (HTTP {Status}), request-id {RequestId}. {GraphMessage}",
            error.Error?.Code,
            error.ResponseStatusCode,
            error.Error?.InnerError?.RequestId,
            error.Error?.Message);

        return new(
            RequestId: string.Empty,
            Status: ActivationStatus.Failed,
            StartDateTime: null,
            EndDateTime: null,
            Error: PimErrorMapper.Map(error));
    }

    private async Task<ActivationResult> SubmitActivationAsync(
        ActivationRequest request,
        DateTimeOffset startDateTime,
        CancellationToken ct)
    {
        var body = new PrivilegedAccessGroupAssignmentScheduleRequest
        {
            Action = ScheduleRequestActions.SelfActivate,
            AccessId = ToAccessId(request.Eligibility.Kind),
            PrincipalId = request.Eligibility.PrincipalId,
            GroupId = request.Eligibility.ResourceId,
            Justification = BuildJustification(request),
            IsValidationOnly = request.IsValidationOnly,
            ScheduleInfo = new RequestSchedule
            {
                StartDateTime = startDateTime,
                Expiration = new ExpirationPattern
                {
                    Type = ExpirationPatternType.AfterDuration,
                    Duration = request.Duration,
                },
            },
        };

        var response = await _graph.IdentityGovernance.PrivilegedAccess.Group.AssignmentScheduleRequests
            .PostAsync(
                body,
                requestConfiguration =>
                {
                    if (request.AuthContextClaim is { } acrs)
                    {
                        // The policy demands a Conditional Access authentication
                        // context; the token for this POST must carry the acrs
                        // claim or Graph rejects it with HTTP 400 (not a 401
                        // claims challenge). Never log the acrs value itself.
                        _logger.LogInformation(
                            "Activation requires authentication-context step-up; acquiring token with claims");
                        requestConfiguration.Options.Add(new AuthContextRequestOption
                        {
                            ClaimsJson = ClaimsChallengeParser.BuildAuthContextClaims(acrs),
                        });
                    }
                },
                ct)
            .ConfigureAwait(false);

        return new ActivationResult(
            RequestId: response?.Id ?? string.Empty,
            Status: ActivationStatusParser.Parse(response?.Status),
            StartDateTime: startDateTime,
            EndDateTime: startDateTime + request.Duration,
            Error: null);
    }
}
