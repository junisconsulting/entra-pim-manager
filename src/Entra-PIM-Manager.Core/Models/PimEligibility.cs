namespace EntraPimManager.Core.Models;

/// <summary>
/// A single thing the signed-in user is eligible to activate — unifies directory
/// roles and PIM-for-Groups access into one model so the UI has a single list type.
/// </summary>
/// <param name="Kind">Which API surface this eligibility belongs to.</param>
/// <param name="DisplayName">Role display name, or resolved group display name.</param>
/// <param name="ResourceId">Role definition id (directory role) or group id (group access).</param>
/// <param name="ScopeId">
/// Directory scope id (e.g. <c>/</c> or <c>/administrativeUnits/{id}</c>) for directory
/// roles, or the group id for group access. Passed verbatim to activation — never normalized.
/// </param>
/// <param name="PrincipalId">The signed-in user's Entra object id (oid).</param>
/// <param name="EndDateTime">When the eligibility window ends, if bounded.</param>
/// <param name="IsRoleAssignableGroup">
/// True when this is a role-assignable group — activating it may grant directory roles.
/// </param>
public sealed record PimEligibility(
    PimResourceKind Kind,
    string DisplayName,
    string ResourceId,
    string ScopeId,
    string PrincipalId,
    DateTimeOffset? EndDateTime,
    bool IsRoleAssignableGroup);
