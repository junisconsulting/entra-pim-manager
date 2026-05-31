namespace EntraPimManager.Core.Models;

/// <summary>
/// Minimal directory metadata for a group, resolved in a batch lookup because
/// <c>$expand=group</c> on the PIM-for-Groups endpoints is unreliable.
/// </summary>
/// <param name="Id">The group's object id.</param>
/// <param name="DisplayName">The group's display name.</param>
/// <param name="IsAssignableToRole">
/// True when the group is role-assignable — activating it may grant directory roles.
/// </param>
public sealed record GroupInfo(
    string Id,
    string DisplayName,
    bool IsAssignableToRole);
