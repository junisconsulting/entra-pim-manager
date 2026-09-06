namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Arm;
using EntraPimManager.Core.Caching;
using EntraPimManager.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

/// <summary>
/// Reads role-management policy assignments and parses the end-user activation
/// rules into an <see cref="ActivationPolicy"/>. Graph kinds are read here;
/// Azure resource roles are read through <see cref="IPimAzureResourceService"/>.
/// Results are cached via <see cref="PolicyCache"/>.
/// </summary>
public sealed class PolicyService : IPolicyService
{
    private const string ExpirationRuleId = "Expiration_EndUser_Assignment";
    private const string EnablementRuleId = "Enablement_EndUser_Assignment";
    private const string ApprovalRuleId = "Approval_EndUser_Assignment";
    private const string AuthContextRuleId = "AuthenticationContext_EndUser_Assignment";

    private readonly GraphServiceClient _graph;
    private readonly IPimAzureResourceService _azureResourceService;
    private readonly PolicyCache _cache;
    private readonly ILogger<PolicyService> _logger;

    public PolicyService(
        GraphServiceClient graph,
        IPimAzureResourceService azureResourceService,
        PolicyCache cache,
        ILogger<PolicyService> logger)
    {
        _graph = graph;
        _azureResourceService = azureResourceService;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ActivationPolicy> GetPolicyAsync(
        string tenantId,
        PimResourceKind kind,
        string resourceId,
        string scopeId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);

        var cached = _cache.Get(tenantId, kind, resourceId, scopeId);
        if (cached is not null)
        {
            return cached;
        }

        ActivationPolicy policy;
        try
        {
            policy = kind == PimResourceKind.AzureResourceRole
                ? await _azureResourceService.GetPolicyAsync(scopeId, resourceId, ct).ConfigureAwait(false)
                : await ReadGraphPolicyAsync(kind, resourceId, ct).ConfigureAwait(false);
        }
        catch (ODataError error)
        {
            policy = DefaultAfterReadFailure(kind, tenantId, error.Error?.Code, error.ResponseStatusCode);
        }
        catch (ArmRequestException error)
        {
            policy = DefaultAfterReadFailure(kind, tenantId, error.Code, error.StatusCode);
        }

        _cache.Set(tenantId, kind, resourceId, scopeId, policy);
        return policy;
    }

    private static ActivationPolicy ParsePolicyRules(IList<UnifiedRoleManagementPolicyRule>? rules)
    {
        // Start from defaults; only the *_EndUser_Assignment rules are relevant.
        var policy = new ActivationPolicy();
        if (rules is null)
        {
            return policy;
        }

        foreach (var rule in rules)
        {
            switch (rule.Id)
            {
                case ExpirationRuleId when rule is UnifiedRoleManagementPolicyExpirationRule expiration:
                    policy = policy with
                    {
                        MaximumDuration = expiration.MaximumDuration ?? policy.MaximumDuration,
                    };
                    break;

                case EnablementRuleId when rule is UnifiedRoleManagementPolicyEnablementRule enablement:
                    policy = policy with
                    {
                        RequiresJustification = enablement.EnabledRules?.Contains("Justification") ?? false,
                        RequiresTicketInfo = enablement.EnabledRules?.Contains("Ticketing") ?? false,
                        RequiresMfa = enablement.EnabledRules?.Contains("MultiFactorAuthentication") ?? false,
                    };
                    break;

                case ApprovalRuleId when rule is UnifiedRoleManagementPolicyApprovalRule approval:
                    policy = policy with
                    {
                        RequiresApproval = approval.Setting?.IsApprovalRequired ?? false,
                    };
                    break;

                case AuthContextRuleId when rule is UnifiedRoleManagementPolicyAuthenticationContextRule authContext:
                    policy = policy with
                    {
                        RequiresAuthContext = authContext.IsEnabled ?? false,
                        AuthContextClaim = authContext.ClaimValue,
                    };
                    break;

                default:
                    break;
            }
        }

        return policy;
    }

    private async Task<ActivationPolicy> ReadGraphPolicyAsync(
        PimResourceKind kind,
        string resourceId,
        CancellationToken ct)
    {
        // A group carries two independent policies — one for 'member', one for
        // 'owner' — both at the same scope. Filtering on the group alone returns
        // both, and taking the first would apply the owner's rules to a
        // membership activation (or vice versa) in unspecified order.
        var groupRole = kind == PimResourceKind.GroupOwnership ? "owner" : "member";
        var filter = kind == PimResourceKind.DirectoryRole
            ? $"scopeId eq '/' and scopeType eq 'Directory' and roleDefinitionId eq '{resourceId}'"
            : $"scopeId eq '{resourceId}' and scopeType eq 'Group' and roleDefinitionId eq '{groupRole}'";

        var response = await _graph.Policies.RoleManagementPolicyAssignments
            .GetAsync(
                requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Filter = filter;
                    requestConfiguration.QueryParameters.Expand = ["policy($expand=rules)"];
                },
                ct)
            .ConfigureAwait(false);

        var assignment = response?.Value?.FirstOrDefault();
        return ParsePolicyRules(assignment?.Policy?.Rules);
    }

    /// <summary>
    /// The policy only decides what the activation form offers — PIM enforces
    /// the real rules on the request itself. A tenant that has not consented to
    /// the policy scope (PermissionScopeNotGranted, or AuthorizationFailed on
    /// ARM) must therefore still reach the form, with the conservative defaults,
    /// rather than hit a dead end on click.
    /// </summary>
    private ActivationPolicy DefaultAfterReadFailure(PimResourceKind kind, string tenantId, string? code, int status)
    {
        // AuthorizationFailed on an ARM policy read is the normal answer for a user
        // who is only eligible — see the azure-rbac-pim-arm-api skill. Warning it is
        // both wrong and loud: with one policy per (role, scope) a large estate turns
        // the expected case into hundreds of warnings a cycle.
        var expected = string.Equals(code, "AuthorizationFailed", StringComparison.OrdinalIgnoreCase);
        _logger.Log(
            expected ? LogLevel.Debug : LogLevel.Warning,
            "Policy read failed for {Kind} in tenant {TenantId}: {Code} (HTTP {Status}). Using default activation limits.",
            kind,
            tenantId,
            code,
            status);
        return new ActivationPolicy();
    }
}
