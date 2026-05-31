namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Models;

/// <summary>
/// Read and activation access to the PIM-for-Groups surface for the signed-in user.
/// </summary>
public interface IPimGroupService
{
    /// <summary>Lists the group memberships/ownerships the user is eligible to activate.</summary>
    Task<IReadOnlyList<PimEligibility>> GetEligibleGroupAccessAsync(CancellationToken ct = default);

    /// <summary>Lists the group memberships/ownerships currently PIM-activated for the user.</summary>
    Task<IReadOnlyList<ActiveAssignment>> GetActiveGroupAccessAsync(CancellationToken ct = default);

    /// <summary>Submits a self-activation request for a group-access eligibility.</summary>
    Task<ActivationResult> ActivateAsync(ActivationRequest request, CancellationToken ct = default);

    /// <summary>Submits a self-deactivation request for an active group assignment.</summary>
    Task<ActivationResult> DeactivateAsync(ActiveAssignment assignment, CancellationToken ct = default);
}
