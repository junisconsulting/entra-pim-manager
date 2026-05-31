namespace EntraPimManager.Core.Models;

/// <summary>
/// Discriminator for the two parallel PIM API surfaces a user can activate against.
/// </summary>
public enum PimResourceKind
{
    /// <summary>An Entra directory role (e.g. Global Administrator).</summary>
    DirectoryRole,

    /// <summary>Membership of a PIM-managed group.</summary>
    GroupMembership,

    /// <summary>Ownership of a PIM-managed group.</summary>
    GroupOwnership,
}
