namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Models;

/// <summary>
/// Maps the Graph or Azure Resource Manager schedule-request <c>status</c> string
/// to <see cref="ActivationStatus"/>. Both surfaces share the vocabulary; ARM adds
/// a few in-flight states for a request it has accepted but not yet provisioned,
/// which the UI treats like a pending schedule creation.
/// </summary>
internal static class ActivationStatusParser
{
    public static ActivationStatus Parse(string? status) => status switch
    {
        "Provisioned" => ActivationStatus.Provisioned,
        "Granted" => ActivationStatus.Granted,
        "PendingApproval" or "PendingApprovalProvisioning" => ActivationStatus.PendingApproval,
        "PendingScheduleCreation" or "Accepted" or "PendingEvaluation" or "PendingProvisioning"
            or "ProvisioningStarted" or "ScheduleCreated" => ActivationStatus.PendingScheduleCreation,
        "Denied" => ActivationStatus.Denied,
        "Failed" => ActivationStatus.Failed,
        "Revoked" => ActivationStatus.Revoked,
        _ => ActivationStatus.Unknown,
    };
}
