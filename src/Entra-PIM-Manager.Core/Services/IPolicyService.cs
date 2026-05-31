namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Models;

/// <summary>
/// Resolves the PIM activation policy for a directory role or group, scoped to
/// a specific tenant.
/// </summary>
public interface IPolicyService
{
    /// <summary>
    /// Returns the activation policy for the given resource in the given tenant.
    /// For <see cref="PimResourceKind.DirectoryRole"/> the resource id is the role
    /// definition id; for group kinds it is the group id. Results are cached briefly
    /// using a tenant-scoped key.
    /// </summary>
    Task<ActivationPolicy> GetPolicyAsync(
        string tenantId,
        PimResourceKind kind,
        string resourceId,
        CancellationToken ct = default);
}
