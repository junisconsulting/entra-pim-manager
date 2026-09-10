namespace EntraPimManager.Core.ErrorHandling;

using EntraPimManager.Core.Arm;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Identity.Client;

/// <summary>
/// Translates Graph <see cref="ODataError"/> and Azure Resource Manager
/// <see cref="ArmRequestException"/> responses into <see cref="UserFacingError"/>
/// based on the <c>error.code</c> and HTTP status. The two surfaces share most
/// PIM error codes. Raw service messages never reach the UI.
/// </summary>
public static class PimErrorMapper
{
    private const string MfaMessage = "This activation requires MFA verification. Please re-authenticate.";
    private const string JustificationMessage = "A justification is required.";
    private const string TicketMessage = "A ticket reference is required.";
    private const string DurationMessage = "The requested duration exceeds the allowed maximum.";
    private const string RefreshMessage = "Eligibility no longer available. Please refresh the list.";
    private const string GenericActivationMessage = "Activation failed. See the log file for details.";

    /// <summary>Maps a Graph error to a user-facing error.</summary>
    public static UserFacingError Map(ODataError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return Map(error.Error?.Code ?? string.Empty, error.ResponseStatusCode, error.Error?.Message);
    }

    /// <summary>Maps an Azure Resource Manager error to a user-facing error.</summary>
    public static UserFacingError Map(ArmRequestException error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return Map(error.Code, error.StatusCode, error.Detail);
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
            ArmRequestException armError => Map(armError),

            // Must precede the MsalServiceException arm: a WAM prompt the user
            // dismissed can surface as either MsalClientException or
            // MsalServiceException, both deriving from MsalException.
            MsalException { ErrorCode: MsalError.AuthenticationCanceledError } =>
                Error(ErrorSeverity.Info, "Verification was cancelled. The role was not activated."),

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
    /// Returns the message to show for <paramref name="error"/> on an account
    /// enrolled with <paramref name="authMethod"/>. A device-code enrollment
    /// cannot satisfy a Conditional Access authentication context — that token
    /// path has no way to carry the required claim — so the generic "please try
    /// again" would send the user into a loop that can never succeed. Name the
    /// cause and the way out instead.
    /// </summary>
    public static string Describe(UserFacingError error, AuthMethod authMethod)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Severity == ErrorSeverity.StepUpRequired && authMethod == AuthMethod.DeviceCode
            ? "This role requires additional verification, which device-code sign-in cannot provide. "
              + "Remove this account in Settings and add it again using the standard sign-in."
            : error.Message;
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
    /// Returns the caption to show on a tenant group whose eligibility fetch
    /// failed. The one cause worth naming is a tenant without a PIM licence —
    /// otherwise the group shows "(0)" and looks exactly like a working tenant
    /// with nothing to activate. Everything else stays generic; the detail is
    /// in the log.
    /// </summary>
    public static string DescribeFetchFailure(Exception exception, AuthMethod? authMethod = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // Graph refuses PIM reads on an unlicensed tenant with
        // AadPremiumLicenseRequired ("The tenant needs to have Microsoft Entra
        // ID P2 or Microsoft Entra ID Governance license."). The message match
        // is a safety net in case the code differs between the role and group
        // surfaces.
        if (exception is ODataError { Error: { } error }
            && (string.Equals(error.Code, "AadPremiumLicenseRequired", StringComparison.OrdinalIgnoreCase)
                || (error.Message?.Contains("Governance license", StringComparison.OrdinalIgnoreCase) ?? false)))
        {
            return "PIM is not available in this tenant: it has no Microsoft Entra ID P2 or Governance license.";
        }

        // MsalAuthService found no cached MSAL account for this enrollment. The
        // realistic cause is an App Registration change (the tenant's entry got
        // a different client id), which moves the enrollment to a different MSAL
        // client whose cache has never seen it. Only a fresh sign-in can repair
        // that — say so instead of "see the log".
        if (exception is MsalUiRequiredException { ErrorCode: MsalError.UserNullError })
        {
            return "Sign-in for this account is no longer valid (its App Registration may have changed). Remove the account in Settings and add it again.";
        }

        // AADSTS65001: the tenant has not consented to a permission the app now
        // requests — every scope change reopens this window. The refresh token
        // stays valid and consent is checked at token issuance, so the account
        // recovers by itself once an admin consents; say so instead of "see the log".
        if (exception is MsalUiRequiredException
            && exception.Message.Contains("AADSTS65001", StringComparison.Ordinal))
        {
            return "This tenant has not consented to the app's current permissions. An admin must grant admin consent for the App Registration; the account then recovers on its own.";
        }

        // AADSTS53000/53001: a Conditional Access policy demands a compliant or
        // domain-joined device. A device-code token carries no device claim at
        // all, so that enrollment can never satisfy the policy no matter how
        // often the list is refreshed — name the sign-in method as the cause and
        // point at the broker. On a broker account the device really is the
        // problem, so say that instead of sending the user off to re-add an
        // account that would land in exactly the same place. MsalException, not
        // MsalUiRequiredException: MSAL's UiRequired throttle wraps the original
        // in a different type on every repeat of the same failure.
        if (exception is MsalException
            && (exception.Message.Contains("AADSTS53001", StringComparison.Ordinal)
                || exception.Message.Contains("AADSTS53000", StringComparison.Ordinal)))
        {
            return authMethod == AuthMethod.DeviceCode
                ? "A Conditional Access policy requires a managed device, which device-code sign-in cannot present. Remove this account in Settings and add it again using the standard sign-in."
                : "A Conditional Access policy requires a managed device (domain-joined or compliant) and this device does not meet it.";
        }

        // Cancelled while acquiring the ARM token, not while waiting for Azure.
        // Keeping the two apart is what the 2026-09-04 field case cost: a token
        // problem reported as "the request timed out" sends the admin hunting for
        // a network fault that isn't there.
        if (exception is ArmSignInPendingException)
        {
            return "Signing in for this tenant's Azure permission didn't finish. If it keeps happening, the tenant has most likely not consented to the app's Azure permission — an admin must grant it.";
        }

        // Azure Resource Manager refused the read outright — typically
        // AuthorizationFailed while the token lacks the Azure Service
        // Management permission, or a tenant without any Azure subscriptions.
        if (exception is ArmRequestException)
        {
            return "Azure Resource Manager rejected the request. See the log file for details.";
        }

        return "Couldn't load eligibilities for this tenant. See the log file for details.";
    }

    private static UserFacingError Map(string code, int statusCode, string? message) => code switch
    {
        "RoleAssignmentExists" or "RoleAssignmentInstanceAlreadyExists" or "RoleAssignmentAlreadyExists" =>
            Error(ErrorSeverity.Info, "This role is already active."),

        "JustificationRuleViolated" or "JustificationRequired" =>
            Error(ErrorSeverity.Validation, JustificationMessage, "justification"),

        "TicketingRuleViolated" or "TicketInfoRequired" =>
            Error(ErrorSeverity.Validation, TicketMessage, "ticket"),

        "MfaRuleViolated" or "MfaRuleNotSatisfied" or "MfaRequired" =>
            Error(ErrorSeverity.StepUpRequired, MfaMessage),

        "MaximumDurationExceeded" or "ScheduleExpirationRuleViolated" =>
            Error(ErrorSeverity.Validation, DurationMessage, "duration"),

        "StartTimeInPast" or "InvalidStartDateTime" =>
            Error(ErrorSeverity.Validation, "The start time is in the past. Please check the system clock."),

        // ARM folds every policy failure into one code and names the failed
        // rule in the message: 'The following policy rules failed: ["MfaRule"]'.
        "RoleAssignmentRequestPolicyValidationFailed" => MapPolicyRuleFailure(message),

        // ARM: self-deactivation inside Microsoft's 5-minute minimum active duration.
        "ActiveDurationTooShort" =>
            Error(ErrorSeverity.Validation, "The role was activated less than 5 minutes ago and cannot be deactivated yet."),

        "InvalidRoleAssignmentRequestSchedule" =>
            Error(ErrorSeverity.Validation, "The requested activation schedule was rejected. Check the duration and the system clock.", "duration"),

        "InvalidScope" or "ScopeNotAllowed" =>
            Error(ErrorSeverity.Fatal, "Invalid scope for this activation."),

        "EligibilityNotFound" or "RoleAssignmentDoesNotExist" or "ResourceNotFound" =>
            Error(ErrorSeverity.RefreshList, RefreshMessage),

        "ConcurrentActivationInProgress" =>
            Error(ErrorSeverity.RefreshList, "Another activation request is already in progress."),

        "InsufficientPermissions" or "Authorization_RequestDenied" or "AuthorizationFailed" =>
            Error(ErrorSeverity.Fatal, "Missing permission. Please contact your administrator."),

        // The app asked for a scope this tenant never consented to. Only a
        // tenant admin can fix it, and only on the App Registration — say
        // which side the problem is on instead of "contact your admin".
        "PermissionScopeNotGranted" =>
            Error(ErrorSeverity.Fatal, "This tenant has not granted the App Registration all required permissions. An admin must re-grant admin consent for it."),

        "RoleAssignmentApprovalRequired" =>
            Error(ErrorSeverity.Info, "This activation requires approval. The request has been submitted."),

        // RoleAssignmentRequestAcrsValidationFailed on the directory-role and
        // ARM surfaces; substring match also covers whatever the group surface
        // calls it — the exact code lands in the log either way.
        _ when code.Contains("AcrsValidationFailed", StringComparison.OrdinalIgnoreCase) =>
            Error(ErrorSeverity.StepUpRequired, "This activation requires additional identity verification, which could not be completed. Please try again."),

        _ when statusCode == 429 =>
            Error(ErrorSeverity.Throttled, "Too many requests. Please wait a moment."),

        _ when statusCode is 500 or 503 =>
            Error(ErrorSeverity.Fatal, "The Microsoft service is currently unavailable. Please try again later."),

        _ =>
            Error(ErrorSeverity.Fatal, GenericActivationMessage),
    };

    /// <summary>
    /// ARM reports which end-user rule an activation violated only inside the
    /// message text, so the rule name decides the severity and field hint.
    /// </summary>
    private static UserFacingError MapPolicyRuleFailure(string? message)
    {
        var rules = message ?? string.Empty;
        if (rules.Contains("MfaRule", StringComparison.OrdinalIgnoreCase))
        {
            return Error(ErrorSeverity.StepUpRequired, MfaMessage);
        }

        if (rules.Contains("JustificationRule", StringComparison.OrdinalIgnoreCase))
        {
            return Error(ErrorSeverity.Validation, JustificationMessage, "justification");
        }

        if (rules.Contains("TicketingRule", StringComparison.OrdinalIgnoreCase))
        {
            return Error(ErrorSeverity.Validation, TicketMessage, "ticket");
        }

        if (rules.Contains("ExpirationRule", StringComparison.OrdinalIgnoreCase))
        {
            return Error(ErrorSeverity.Validation, DurationMessage, "duration");
        }

        if (rules.Contains("EligibilityRule", StringComparison.OrdinalIgnoreCase))
        {
            return Error(ErrorSeverity.RefreshList, RefreshMessage);
        }

        return Error(ErrorSeverity.Fatal, GenericActivationMessage);
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
        // Raised by MsalAuthService before it can even build a PCA: no App
        // Registration is pinned to the tenant (typically an enrollment whose
        // entry was removed from Settings). The message names tenant and cloud.
        if (msal.ErrorCode == "app_registration_missing")
        {
            return Error(
                ErrorSeverity.Fatal,
                $"{msal.Message} Open Settings → Tenants and add an entry for that tenant.");
        }

        // Raised by MsalAuthService when Entra issued the token for a different
        // tenant than the entry asked for. Should not happen with .WithTenantId;
        // the enrollment was discarded rather than persisted under the wrong entry.
        if (msal.ErrorCode == "tenant_mismatch")
        {
            return Error(
                ErrorSeverity.Fatal,
                "The sign-in ended up in a different tenant than the selected entry. Check the entry's tenant id in Settings → Tenants and try again.");
        }

        // AADSTS700016: the client id is unknown in the directory it was sent to.
        // Either a cloud mismatch — a Global client id cannot exist in the 21Vianet
        // directory (and vice versa) — or a single-tenant client id paired with
        // the wrong tenant id, or the tenant simply never consented to the app.
        if (msal.Message.Contains("AADSTS700016", StringComparison.Ordinal))
        {
            return Error(
                ErrorSeverity.Fatal,
                "This app registration is unknown in the selected tenant. Check the entry's tenant id, client id and cloud in Settings → Tenants, and that admin consent was granted in that tenant.");
        }

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
