namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Caching;
using EntraPimManager.Core.Models;
using Microsoft.Graph;
using Microsoft.Graph.Models;

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

    public PolicyService(GraphServiceClient graph, PolicyCache cache)
    {
        _graph = graph;
        _cache = cache;
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

        var filter = kind == PimResourceKind.DirectoryRole
            ? $"scopeId eq '/' and scopeType eq 'Directory' and roleDefinitionId eq '{resourceId}'"
            : $"scopeId eq '{resourceId}' and scopeType eq 'Group'";

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
        var policy = ParsePolicyRules(assignment?.Policy?.Rules);
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
