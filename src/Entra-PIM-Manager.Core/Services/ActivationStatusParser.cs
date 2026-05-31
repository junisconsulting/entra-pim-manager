namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Models;

/// <summary>
/// Maps the Graph schedule-request <c>status</c> string to <see cref="ActivationStatus"/>.
/// </summary>
internal static class ActivationStatusParser
{
    public static ActivationStatus Parse(string? status) => status switch
    {
        "Provisioned" => ActivationStatus.Provisioned,
        "Granted" => ActivationStatus.Granted,
        "PendingApproval" => ActivationStatus.PendingApproval,
        "PendingScheduleCreation" => ActivationStatus.PendingScheduleCreation,
        "Denied" => ActivationStatus.Denied,
        "Failed" => ActivationStatus.Failed,
        "Revoked" => ActivationStatus.Revoked,
        _ => ActivationStatus.Unknown,
    };
}
