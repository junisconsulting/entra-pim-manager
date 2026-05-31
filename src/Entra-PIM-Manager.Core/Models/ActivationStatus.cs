namespace EntraPimManager.Core.Models;

/// <summary>
/// Outcome status of an activation request, parsed from the response body
/// (independent of the HTTP status code — a 201 can still be PendingApproval).
/// </summary>
public enum ActivationStatus
{
    /// <summary>Status could not be determined.</summary>
    Unknown,

    /// <summary>The role/access is active right now.</summary>
    Provisioned,

    /// <summary>Scheduled — the start time is in the future.</summary>
    Granted,

    /// <summary>Waiting for an approver to act.</summary>
    PendingApproval,

    /// <summary>Initial state; will transition to another status shortly.</summary>
    PendingScheduleCreation,

    /// <summary>An approver rejected the request.</summary>
    Denied,

    /// <summary>Provisioning failed.</summary>
    Failed,

    /// <summary>Was active and has since ended.</summary>
    Revoked,
}
