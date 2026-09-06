namespace EntraPimManager.Core.Models;

/// <summary>
/// Discriminator for the three parallel PIM API surfaces a user can activate against.
/// New values go at the end: justification favourites persist the kind as its integer value.
/// </summary>
public enum PimResourceKind
{
    /// <summary>An Entra directory role (e.g. Global Administrator).</summary>
    DirectoryRole,

    /// <summary>Membership of a PIM-managed group.</summary>
    GroupMembership,

    /// <summary>Ownership of a PIM-managed group.</summary>
    GroupOwnership,

    /// <summary>
    /// An Azure RBAC role at a management-group, subscription, resource-group or
    /// resource scope, activated through Azure Resource Manager rather than Graph.
    /// </summary>
    AzureResourceRole,
}
