namespace EntraPimManager.Core.Models;

/// <summary>
/// A currently-active, PIM-activated assignment (<c>assignmentType eq 'Activated'</c>).
/// Permanent <c>Assigned</c> assignments are excluded — the user cannot deactivate those.
/// </summary>
/// <param name="Kind">Which API surface this assignment belongs to.</param>
/// <param name="DisplayName">Role display name, or resolved group display name.</param>
/// <param name="ResourceId">Role definition id (directory role) or group id (group access).</param>
/// <param name="ScopeId">Directory scope id, or the group id for group access.</param>
/// <param name="PrincipalId">The signed-in user's Entra object id (oid).</param>
/// <param name="StartDateTime">When the activation started.</param>
/// <param name="EndDateTime">When the activation expires — drives the countdown UI.</param>
/// <param name="AssignmentScheduleId">Id of the underlying assignment schedule instance.</param>
public sealed record ActiveAssignment(
    PimResourceKind Kind,
    string DisplayName,
    string ResourceId,
    string ScopeId,
    string PrincipalId,
    DateTimeOffset? StartDateTime,
    DateTimeOffset? EndDateTime,
    string AssignmentScheduleId);
