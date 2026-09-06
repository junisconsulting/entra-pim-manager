namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Models;

/// <summary>
/// Resolves the PIM activation policy for a directory role, group or Azure
/// resource role, scoped to a specific tenant.
/// </summary>
public interface IPolicyService
{
    /// <summary>
    /// Returns the activation policy for the given resource in the given tenant.
    /// For <see cref="PimResourceKind.DirectoryRole"/> the resource id is the role
    /// definition id; for group kinds it is the group id; for
    /// <see cref="PimResourceKind.AzureResourceRole"/> it is the ARM role definition
    /// id and <paramref name="scopeId"/> the ARM scope — the same role carries a
    /// different policy per scope. Results are cached briefly using a
    /// tenant-and-scope-scoped key.
    /// </summary>
    Task<ActivationPolicy> GetPolicyAsync(
        string tenantId,
        PimResourceKind kind,
        string resourceId,
        string scopeId,
        CancellationToken ct = default);
}
