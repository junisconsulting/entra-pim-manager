namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.ErrorHandling;
using EntraPimManager.Core.Models;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using GraphTicketInfo = Microsoft.Graph.Models.TicketInfo;

/// <summary>
/// Reads and activates the directory-roles PIM surface (<c>/roleManagement/directory/*</c>).
/// </summary>
public sealed class PimRoleService : IPimRoleService
{
    private const string UnknownRoleName = "(unknown role)";
    private readonly GraphServiceClient _graph;

    public PimRoleService(GraphServiceClient graph)
    {
        _graph = graph;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PimEligibility>> GetEligibleRolesAsync(CancellationToken ct = default)
    {
        var response = await _graph.RoleManagement.Directory.RoleEligibilityScheduleInstances
            .FilterByCurrentUserWithOn("principal")
            .GetAsFilterByCurrentUserWithOnGetResponseAsync(
                requestConfiguration => requestConfiguration.QueryParameters.Expand = ["roleDefinition"],
                ct)
            .ConfigureAwait(false);

        var instances = response?.Value ?? [];
        return instances.Select(ToEligibility).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActiveAssignment>> GetActiveRolesAsync(CancellationToken ct = default)
    {
        var response = await _graph.RoleManagement.Directory.RoleAssignmentScheduleInstances
            .FilterByCurrentUserWithOn("principal")
            .GetAsFilterByCurrentUserWithOnGetResponseAsync(
                requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Expand = ["roleDefinition"];
                    requestConfiguration.QueryParameters.Filter = "assignmentType eq 'Activated'";
                },
                ct)
            .ConfigureAwait(false);

        var instances = response?.Value ?? [];
        return instances.Select(ToActiveAssignment).ToList();
    }

    /// <inheritdoc />
    public async Task<ActivationResult> ActivateAsync(ActivationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Eligibility.Kind != PimResourceKind.DirectoryRole)
        {
            throw new ArgumentException("Expected a directory-role eligibility.", nameof(request));
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
        if (assignment.Kind != PimResourceKind.DirectoryRole)
        {
            throw new ArgumentException("Expected a directory-role assignment.", nameof(assignment));
        }

        var body = new UnifiedRoleAssignmentScheduleRequest
        {
            Action = UnifiedRoleScheduleRequestActions.SelfDeactivate,
            PrincipalId = assignment.PrincipalId,
            RoleDefinitionId = assignment.ResourceId,
            DirectoryScopeId = assignment.ScopeId,
        };

        try
        {
            var response = await _graph.RoleManagement.Directory.RoleAssignmentScheduleRequests
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

    private static PimEligibility ToEligibility(UnifiedRoleEligibilityScheduleInstance instance) => new(
        Kind: PimResourceKind.DirectoryRole,
        DisplayName: instance.RoleDefinition?.DisplayName ?? UnknownRoleName,
        ResourceId: instance.RoleDefinitionId ?? string.Empty,
        ScopeId: instance.DirectoryScopeId ?? "/",
        PrincipalId: instance.PrincipalId ?? string.Empty,
        EndDateTime: instance.EndDateTime,
        IsRoleAssignableGroup: false);

    private static ActiveAssignment ToActiveAssignment(UnifiedRoleAssignmentScheduleInstance instance) => new(
        Kind: PimResourceKind.DirectoryRole,
        DisplayName: instance.RoleDefinition?.DisplayName ?? UnknownRoleName,
        ResourceId: instance.RoleDefinitionId ?? string.Empty,
        ScopeId: instance.DirectoryScopeId ?? "/",
        PrincipalId: instance.PrincipalId ?? string.Empty,
        StartDateTime: instance.StartDateTime,
        EndDateTime: instance.EndDateTime,
        AssignmentScheduleId: instance.Id ?? string.Empty);

    private static ActivationResult Failure(ODataError error) => new(
        RequestId: string.Empty,
        Status: ActivationStatus.Failed,
        StartDateTime: null,
        EndDateTime: null,
        Error: PimErrorMapper.Map(error));

    private async Task<ActivationResult> SubmitActivationAsync(
        ActivationRequest request,
        DateTimeOffset startDateTime,
        CancellationToken ct)
    {
        var body = new UnifiedRoleAssignmentScheduleRequest
        {
            Action = UnifiedRoleScheduleRequestActions.SelfActivate,
            PrincipalId = request.Eligibility.PrincipalId,
            RoleDefinitionId = request.Eligibility.ResourceId,

            // directoryScopeId is passed through verbatim — never normalized.
            DirectoryScopeId = request.Eligibility.ScopeId,
            Justification = request.Justification,
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
            TicketInfo = request.Ticket is null
                ? null
                : new GraphTicketInfo
                {
                    TicketNumber = request.Ticket.TicketNumber,
                    TicketSystem = request.Ticket.TicketSystem,
                },
        };

        var response = await _graph.RoleManagement.Directory.RoleAssignmentScheduleRequests
            .PostAsync(body, cancellationToken: ct)
            .ConfigureAwait(false);

        return new ActivationResult(
            RequestId: response?.Id ?? string.Empty,
            Status: ActivationStatusParser.Parse(response?.Status),
            StartDateTime: startDateTime,
            EndDateTime: startDateTime + request.Duration,
            Error: null);
    }
}
