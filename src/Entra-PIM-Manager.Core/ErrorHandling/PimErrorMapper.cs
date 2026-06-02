namespace EntraPimManager.Core.ErrorHandling;

using EntraPimManager.Core.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Identity.Client;

/// <summary>
/// Translates Graph <see cref="ODataError"/> responses into <see cref="UserFacingError"/>
/// based on the <c>error.code</c> and HTTP status. Raw Graph messages never reach the UI.
/// </summary>
public static class PimErrorMapper
{
    /// <summary>Maps a Graph error to a user-facing error.</summary>
    public static UserFacingError Map(ODataError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var code = error.Error?.Code ?? string.Empty;
        var statusCode = error.ResponseStatusCode;

        return code switch
        {
            "RoleAssignmentExists" or "RoleAssignmentInstanceAlreadyExists" or "RoleAssignmentAlreadyExists" =>
                Error(ErrorSeverity.Info, "This role is already active."),

            "JustificationRuleViolated" or "JustificationRequired" =>
                Error(ErrorSeverity.Validation, "A justification is required.", "justification"),

            "TicketingRuleViolated" or "TicketInfoRequired" =>
                Error(ErrorSeverity.Validation, "A ticket reference is required.", "ticket"),

            "MfaRuleViolated" or "MfaRuleNotSatisfied" or "MfaRequired" =>
                Error(ErrorSeverity.StepUpRequired, "This activation requires MFA verification. Please re-authenticate."),

            "MaximumDurationExceeded" or "ScheduleExpirationRuleViolated" =>
                Error(ErrorSeverity.Validation, "The requested duration exceeds the allowed maximum.", "duration"),

            "StartTimeInPast" or "InvalidStartDateTime" =>
                Error(ErrorSeverity.Validation, "The start time is in the past. Please check the system clock."),

            "InvalidScope" or "ScopeNotAllowed" =>
                Error(ErrorSeverity.Fatal, "Invalid scope for this activation."),

            "EligibilityNotFound" or "RoleAssignmentDoesNotExist" or "ResourceNotFound" =>
                Error(ErrorSeverity.RefreshList, "Eligibility no longer available. Please refresh the list."),

            "ConcurrentActivationInProgress" =>
                Error(ErrorSeverity.RefreshList, "Another activation request is already in progress."),

            "InsufficientPermissions" or "Authorization_RequestDenied" =>
                Error(ErrorSeverity.Fatal, "Missing permission. Please contact your administrator."),

            "RoleAssignmentApprovalRequired" =>
                Error(ErrorSeverity.Info, "This activation requires approval. The request has been submitted."),

            _ when statusCode == 429 =>
                Error(ErrorSeverity.Throttled, "Too many requests. Please wait a moment."),

            _ when statusCode is 500 or 503 =>
                Error(ErrorSeverity.Fatal, "The Microsoft service is currently unavailable. Please try again later."),

            _ =>
                Error(ErrorSeverity.Fatal, "Activation failed. See the log file for details."),
        };
    }

    /// <summary>
    /// Maps any exception raised by a Graph call into a <see cref="UserFacingError"/>.
    /// Covers the non-<see cref="ODataError"/> failure paths the UI must still present
    /// gracefully: a cancelled/timed-out operation and a missing network connection.
    /// Raw exception detail never reaches the UI — only the log.
    /// </summary>
    public static UserFacingError MapException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            ODataError odataError => Map(odataError),

            MsalServiceException msal => MapMsal(msal),

            OperationCanceledException =>
                Error(ErrorSeverity.Timeout, "The request timed out. Please try again."),

            _ when IsNetworkFailure(exception) =>
                Error(ErrorSeverity.Offline, "No connection to Microsoft Entra. Please check your network connection."),

            _ =>
                Error(ErrorSeverity.Fatal, "An unexpected error occurred. See the log file for details."),
        };
    }

    /// <summary>
    /// Returns true when the error indicates the request start time was in the past
    /// (typically client/Microsoft clock skew) — a candidate for a single retry.
    /// </summary>
    public static bool IsStartTimeInPast(ODataError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var code = error.Error?.Code;
        return string.Equals(code, "StartTimeInPast", StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, "InvalidStartDateTime", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Walks the exception chain looking for a connectivity failure (a failed HTTP
    /// request or a socket error), which the Graph SDK surfaces wrapped at varying depths.
    /// </summary>
    private static bool IsNetworkFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException or System.Net.Sockets.SocketException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Maps an MSAL sign-in/token error (e.g. from the device-code or broker flow)
    /// into a user-facing message. Keyed on the Entra <c>AADSTS</c> code where a
    /// specific, actionable hint helps the admin fix their own configuration.
    /// </summary>
    private static UserFacingError MapMsal(MsalServiceException msal)
    {
        // AADSTS7000218: the token endpoint demanded a client secret/assertion,
        // i.e. the app registration is treated as a confidential client. A desktop
        // app is a PUBLIC client and cannot ship a secret — the registration is
        // missing "Allow public client flows". This is the federated-IdP /
        // device-code escape-hatch failure mode (see federated-idp-sso-wrong-account).
        if (msal.ErrorCode == MsalError.InvalidClient
            || msal.Message.Contains("AADSTS7000218", StringComparison.Ordinal))
        {
            return Error(
                ErrorSeverity.Fatal,
                "This app registration is not configured for desktop sign-in. In Entra, open the app registration → Authentication → enable \"Allow public client flows\", then try again.");
        }

        // AADSTS70016 is the normal "user hasn't entered the code yet" poll
        // response; MSAL handles it internally and it should never surface here.
        // Any other service error: keep it generic, the detail is in the log.
        return Error(
            ErrorSeverity.Fatal,
            "Sign-in failed. See the log file for details.");
    }

    private static UserFacingError Error(ErrorSeverity severity, string message, string? fieldHint = null) =>
        new(severity, message, fieldHint);
}
