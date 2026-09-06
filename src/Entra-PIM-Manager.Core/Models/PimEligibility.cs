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
/// <param name="ScopeLabel">
/// Human-readable scope for Azure resource roles (e.g. <c>Resource group: rg-prod</c>);
/// <c>null</c> for the Graph surfaces. Display only — never used for matching.
/// </param>
public sealed record PimEligibility(
    PimResourceKind Kind,
    string DisplayName,
    string ResourceId,
    string ScopeId,
    string PrincipalId,
    DateTimeOffset? EndDateTime,
    bool IsRoleAssignableGroup,
    string? ScopeLabel = null)
{
    /// <summary>Prefix Entra uses for a directory scope narrowed to an administrative unit.</summary>
    private const string AdministrativeUnitPrefix = "/administrativeUnits/";

    /// <summary>
    /// True for a directory role that only applies inside one administrative unit rather
    /// than across the directory.
    /// </summary>
    /// <remarks>
    /// The distinction matters more than it looks: a tenant-wide User Administrator and
    /// one scoped to a single administrative unit carry the same role name, and until
    /// this was surfaced the two were indistinguishable in the list. In a
    /// privileged-access tool, "which one am I about to activate" is not a detail.
    /// </remarks>
    public bool IsAdministrativeUnitScoped => Kind == PimResourceKind.DirectoryRole
        && ScopeId.StartsWith(AdministrativeUnitPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The directory scope of a directory role that does **not** apply to the whole
    /// directory — an administrative unit, or a single application — or <c>null</c> for a
    /// tenant-wide role and for the other surfaces, whose scope travels in
    /// <see cref="ScopeLabel"/>.
    /// </summary>
    /// <remarks>
    /// Entra passes <c>directoryScopeId</c> through verbatim and <c>/</c> means the whole
    /// directory. Anything else narrows the grant, and a narrowed grant that looks exactly
    /// like the tenant-wide one is the ambiguity worth removing — administrative units are
    /// only the most common case of it.
    /// </remarks>
    public string? NarrowedScopeId => Kind == PimResourceKind.DirectoryRole
        && !string.IsNullOrEmpty(ScopeId)
        && !string.Equals(ScopeId, "/", StringComparison.Ordinal)
        ? ScopeId
        : null;

    /// <summary>
    /// Object id of the administrative unit this role is scoped to, or <c>null</c> when it
    /// is not AU-scoped. The unit's display name is not resolved — that needs a directory
    /// read the app does not ask consent for.
    /// </summary>
    public string? AdministrativeUnitId => IsAdministrativeUnitScoped
        ? ScopeId[AdministrativeUnitPrefix.Length..]
        : null;
}
