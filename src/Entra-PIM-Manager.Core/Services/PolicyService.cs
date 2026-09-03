namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Caching;
using EntraPimManager.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

/// <summary>
/// Reads role-management policy assignments and parses the end-user activation
/// rules into an <see cref="ActivationPolicy"/>. Results are cached via
/// <see cref="PolicyCache"/>.
/// </summary>
public sealed class PolicyService : IPolicyService
{
    private const string ExpirationRuleId = "Expiration_EndUser_Assignment";
    private const string EnablementRuleId = "Enablement_EndUser_Assignment";
    private const string ApprovalRuleId = "Approval_EndUser_Assignment";
    private const string AuthContextRuleId = "AuthenticationContext_EndUser_Assignment";

    private readonly GraphServiceClient _graph;
    private readonly PolicyCache _cache;
    private readonly ILogger<PolicyService> _logger;

    public PolicyService(GraphServiceClient graph, PolicyCache cache, ILogger<PolicyService> logger)
    {
        _graph = graph;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ActivationPolicy> GetPolicyAsync(
        string tenantId,
        PimResourceKind kind,
        string resourceId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var cached = _cache.Get(tenantId, kind, resourceId);
        if (cached is not null)
        {
            return cached;
        }

        // A group carries two independent policies — one for 'member', one for
        // 'owner' — both at the same scope. Filtering on the group alone returns
        // both, and taking the first would apply the owner's rules to a
        // membership activation (or vice versa) in unspecified order.
        var groupRole = kind == PimResourceKind.GroupOwnership ? "owner" : "member";
        var filter = kind == PimResourceKind.DirectoryRole
            ? $"scopeId eq '/' and scopeType eq 'Directory' and roleDefinitionId eq '{resourceId}'"
            : $"scopeId eq '{resourceId}' and scopeType eq 'Group' and roleDefinitionId eq '{groupRole}'";

        ActivationPolicy policy;
        try
        {
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
            policy = ParsePolicyRules(assignment?.Policy?.Rules);
        }
        catch (ODataError error)
        {
            // The policy only decides what the activation form offers — PIM
            // enforces the real rules on the request itself. A tenant that has
            // not consented to the policy scope (PermissionScopeNotGranted) must
            // therefore still reach the form, with the conservative defaults,
            // rather than hit a dead end on click.
            _logger.LogWarning(
                "Policy read failed for {Kind} in tenant {TenantId}: {Code} (HTTP {Status}). Using default activation limits.",
                kind,
                tenantId,
                error.Error?.Code,
                error.ResponseStatusCode);
            policy = new ActivationPolicy();
        }

        _cache.Set(tenantId, kind, resourceId, policy);
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
}
