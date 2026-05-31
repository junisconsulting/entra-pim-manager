namespace EntraPimManager.Core.Models;

/// <summary>
/// Outcome of an activation or deactivation request.
/// </summary>
/// <param name="RequestId">Id of the created schedule request.</param>
/// <param name="Status">Status parsed from the response body.</param>
/// <param name="StartDateTime">When the activation starts, if known.</param>
/// <param name="EndDateTime">When the activation expires, if known.</param>
/// <param name="Error">The mapped error when the request failed; <c>null</c> on success.</param>
public sealed record ActivationResult(
    string RequestId,
    ActivationStatus Status,
    DateTimeOffset? StartDateTime,
    DateTimeOffset? EndDateTime,
    UserFacingError? Error)
{
    /// <summary>True when the request succeeded (no error and a non-terminal status).</summary>
    public bool IsSuccess => Error is null
        && Status is ActivationStatus.Provisioned
            or ActivationStatus.Granted
            or ActivationStatus.PendingApproval
            or ActivationStatus.PendingScheduleCreation;
}
