namespace EntraPimManager.Core.Models;

/// <summary>
/// Classifies a <see cref="UserFacingError"/> so the UI knows how to present it.
/// </summary>
public enum ErrorSeverity
{
    /// <summary>Informational — not really a failure (e.g. an activation awaiting approval).</summary>
    Info,

    /// <summary>
    /// The role is already active, so nothing happened. Its own value rather than
    /// plain <see cref="Info"/> because a re-activation waits this one out — PIM
    /// keeps refusing until it has released the previous activation — while the
    /// other informational cases (a cancelled verification, a pending approval)
    /// must never be retried.
    /// </summary>
    AlreadyActive,

    /// <summary>Input validation failed — keep the dialog open, highlight the field.</summary>
    Validation,

    /// <summary>A Conditional Access step-up (MFA, auth strength) is required.</summary>
    StepUpRequired,

    /// <summary>The request was throttled — back off and retry later.</summary>
    Throttled,

    /// <summary>The underlying list is stale — refresh eligibilities/assignments.</summary>
    RefreshList,

    /// <summary>No network connection to Microsoft Entra — ask the user to check connectivity.</summary>
    Offline,

    /// <summary>The operation exceeded its time budget and was cancelled.</summary>
    Timeout,

    /// <summary>An unrecoverable error — surface a generic message, log the detail.</summary>
    Fatal,
}
