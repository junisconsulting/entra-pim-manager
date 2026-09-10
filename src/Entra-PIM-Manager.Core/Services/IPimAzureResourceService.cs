namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Models;

/// <summary>
/// Read and activation access to PIM for Azure Resources — Azure RBAC eligibilities
/// at management-group, subscription, resource-group or resource scope — for the
/// signed-in user, through Azure Resource Manager rather than Microsoft Graph.
/// </summary>
public interface IPimAzureResourceService
{
    /// <summary>Lists the Azure resource roles the user is eligible to activate, across all scopes of the tenant.</summary>
    Task<IReadOnlyList<PimEligibility>> GetEligibleAzureRolesAsync(CancellationToken ct = default);

    /// <summary>Lists the Azure resource roles currently PIM-activated for the user.</summary>
    Task<IReadOnlyList<ActiveAssignment>> GetActiveAzureRolesAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists every management group and subscription beneath <paramref name="scopeId"/>
    /// on which the user's eligibility there may be activated instead of on the whole
    /// scope — ARM's <c>eligibleChildResources</c>, which answers with direct children
    /// only, walked down the hierarchy. Ordered by how much they grant: subscriptions
    /// first, grouped under their management group, then the management groups.
    /// </summary>
    Task<IReadOnlyList<EligibleChildScope>> GetEligibleChildScopesAsync(string scopeId, CancellationToken ct = default);

    /// <summary>Submits a self-activation request for an Azure-resource-role eligibility.</summary>
    Task<ActivationResult> ActivateAsync(ActivationRequest request, CancellationToken ct = default);

    /// <summary>Submits a self-deactivation request for an active Azure-resource-role assignment.</summary>
    Task<ActivationResult> DeactivateAsync(ActiveAssignment assignment, CancellationToken ct = default);

    /// <summary>
    /// Reads the role settings for <paramref name="roleDefinitionId"/> at
    /// <paramref name="scopeId"/>. Uncached — <see cref="IPolicyService"/> owns the cache.
    /// </summary>
    Task<ActivationPolicy> GetPolicyAsync(string scopeId, string roleDefinitionId, CancellationToken ct = default);
}
