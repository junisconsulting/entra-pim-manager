namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Auth;
using EntraPimManager.Core.ErrorHandling;
using EntraPimManager.Core.Graph;
using EntraPimManager.Core.Models;
using Microsoft.Extensions.Logging;
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

    /// <summary>Expansions every read asks for: the role's name, and its scope's.</summary>
    private static readonly string[] WithScope = ["roleDefinition", "directoryScope"];

    /// <summary>What is left once a tenant refuses to expand the scope.</summary>
    private static readonly string[] WithoutScope = ["roleDefinition"];

    private readonly GraphServiceClient _graph;
    private readonly ILogger<PimRoleService> _logger;

    // ponytail: whether the role-management delegated scopes alone permit expanding
    // directoryScope is unverified beyond one tenant, and a tenant that refuses must
    // still get its list rather than an error. Drop this the day the expansion is
    // proven — the scope name is then simply always there.
    private bool _scopeExpansionRefused;

    public PimRoleService(GraphServiceClient graph, ILogger<PimRoleService> logger)
    {
        _graph = graph;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PimEligibility>> GetEligibleRolesAsync(CancellationToken ct = default)
    {
        var response = await ReadWithScopeAsync(
            (expand, token) => _graph.RoleManagement.Directory.RoleEligibilityScheduleInstances
                .FilterByCurrentUserWithOn("principal")
                .GetAsFilterByCurrentUserWithOnGetResponseAsync(
                    requestConfiguration => requestConfiguration.QueryParameters.Expand = expand,
                    token),
            ct)
            .ConfigureAwait(false);

        var instances = response?.Value ?? [];
        return instances.Select(ToEligibility).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActiveAssignment>> GetActiveRolesAsync(CancellationToken ct = default)
    {
        var response = await ReadWithScopeAsync(
            (expand, token) => _graph.RoleManagement.Directory.RoleAssignmentScheduleInstances
                .FilterByCurrentUserWithOn("principal")
                .GetAsFilterByCurrentUserWithOnGetResponseAsync(
                    requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Expand = expand;
                        requestConfiguration.QueryParameters.Filter = "assignmentType eq 'Activated'";
                    },
                    token),
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
        IsRoleAssignableGroup: false,
        ScopeLabel: ScopeLabelOf(instance.DirectoryScopeId, instance.DirectoryScope));

    private static ActiveAssignment ToActiveAssignment(UnifiedRoleAssignmentScheduleInstance instance) => new(
        Kind: PimResourceKind.DirectoryRole,
        DisplayName: instance.RoleDefinition?.DisplayName ?? UnknownRoleName,
        ResourceId: instance.RoleDefinitionId ?? string.Empty,
        ScopeId: instance.DirectoryScopeId ?? "/",
        PrincipalId: instance.PrincipalId ?? string.Empty,
        StartDateTime: instance.StartDateTime,
        EndDateTime: instance.EndDateTime,
        AssignmentScheduleId: instance.Id ?? string.Empty,
        ScopeLabel: ScopeLabelOf(instance.DirectoryScopeId, instance.DirectoryScope));

    /// <summary>
    /// Names an administrative-unit or single-object scope. <c>null</c> for a
    /// tenant-wide role, which is what a row without a scope has always meant.
    /// </summary>
    private static string? ScopeLabelOf(string? directoryScopeId, DirectoryObject? scope)
        => DirectoryScopeLabel.For(directoryScopeId, scope?.OdataType, DisplayNameOf(scope));

    /// <summary>
    /// The expanded scope's display name. <see cref="DirectoryObject"/> has none of
    /// its own — the property lives on each derived type — so the known ones are read
    /// directly and anything else through the untyped payload the SDK kept.
    /// </summary>
    private static string? DisplayNameOf(DirectoryObject? scope) => scope switch
    {
        null => null,
        AdministrativeUnit unit => unit.DisplayName,
        Application application => application.DisplayName,
        ServicePrincipal servicePrincipal => servicePrincipal.DisplayName,
        Group group => group.DisplayName,
        _ => scope.AdditionalData?.TryGetValue("displayName", out var value) == true ? value?.ToString() : null,
    };

    /// <summary>
    /// Runs a read with the scope expansion, and once without it if the tenant turns
    /// that down. Losing the scope's name costs a line of detail; losing the list
    /// costs the user every role they have.
    /// </summary>
    private async Task<TResponse?> ReadWithScopeAsync<TResponse>(
        Func<string[], CancellationToken, Task<TResponse?>> read,
        CancellationToken ct)
    {
        if (_scopeExpansionRefused)
        {
            return await read(WithoutScope, ct).ConfigureAwait(false);
        }

        try
        {
            return await read(WithScope, ct).ConfigureAwait(false);
        }
        catch (ODataError error) when (error.ResponseStatusCode is 400 or 403)
        {
            _logger.LogWarning(
                "Expanding directoryScope was refused ({Code}, HTTP {Status}); reading without the scope name from here on.",
                error.Error?.Code,
                error.ResponseStatusCode);
            _scopeExpansionRefused = true;
            return await read(WithoutScope, ct).ConfigureAwait(false);
        }
    }

    private ActivationResult Failure(ODataError error)
    {
        _logger.LogWarning(
            "PIM role request failed: {Code} (HTTP {Status}), request-id {RequestId}. {GraphMessage}",
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
