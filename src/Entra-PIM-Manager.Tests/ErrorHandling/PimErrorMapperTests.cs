namespace EntraPimManager.Tests.ErrorHandling;

using EntraPimManager.Core.Arm;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.ErrorHandling;
using EntraPimManager.Core.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Identity.Client;

public sealed class PimErrorMapperTests
{
    [Theory]
    [InlineData("JustificationRuleViolated", ErrorSeverity.Validation, "justification")]
    [InlineData("TicketingRuleViolated", ErrorSeverity.Validation, "ticket")]
    [InlineData("MaximumDurationExceeded", ErrorSeverity.Validation, "duration")]
    [InlineData("MfaRuleViolated", ErrorSeverity.StepUpRequired, null)]
    [InlineData("RoleAssignmentRequestAcrsValidationFailed", ErrorSeverity.StepUpRequired, null)]
    [InlineData("GroupAssignmentRequestAcrsValidationFailed", ErrorSeverity.StepUpRequired, null)]
    [InlineData("EligibilityNotFound", ErrorSeverity.RefreshList, null)]
    [InlineData("RoleAssignmentExists", ErrorSeverity.Info, null)]
    [InlineData("InsufficientPermissions", ErrorSeverity.Fatal, null)]
    [InlineData("PermissionScopeNotGranted", ErrorSeverity.Fatal, null)]
    public void Map_KnownCode_ReturnsExpectedSeverityAndFieldHint(
        string code,
        ErrorSeverity severity,
        string? fieldHint)
    {
        var error = new ODataError { Error = new MainError { Code = code } };

        var mapped = PimErrorMapper.Map(error);

        Assert.Equal(severity, mapped.Severity);
        Assert.Equal(fieldHint, mapped.FieldHint);
        Assert.NotEmpty(mapped.Message);
    }

    [Fact]
    public void Map_ThrottledStatusCode_ReturnsThrottled()
    {
        var error = new ODataError
        {
            ResponseStatusCode = 429,
            Error = new MainError { Code = "TooManyRequests" },
        };

        var mapped = PimErrorMapper.Map(error);

        Assert.Equal(ErrorSeverity.Throttled, mapped.Severity);
    }

    [Theory]
    [InlineData("RoleAssignmentRequestPolicyValidationFailed", "The following policy rules failed: [\"MfaRule\"]", ErrorSeverity.StepUpRequired, null)]
    [InlineData("RoleAssignmentRequestPolicyValidationFailed", "The following policy rules failed: [\"JustificationRule\"]", ErrorSeverity.Validation, "justification")]
    [InlineData("RoleAssignmentRequestPolicyValidationFailed", "The following policy rules failed: [\"TicketingRule\"]", ErrorSeverity.Validation, "ticket")]
    [InlineData("RoleAssignmentRequestPolicyValidationFailed", "The following policy rules failed: [\"ExpirationRule\"]", ErrorSeverity.Validation, "duration")]
    [InlineData("RoleAssignmentRequestPolicyValidationFailed", "The following policy rules failed: [\"EligibilityRule\"]", ErrorSeverity.RefreshList, null)]
    [InlineData("RoleAssignmentRequestPolicyValidationFailed", "The following policy rules failed: [\"SomethingNew\"]", ErrorSeverity.Fatal, null)]
    [InlineData("RoleAssignmentRequestAcrsValidationFailed", "Reauthenticate with claims=%7B%22access_token%22%3A%7B%22acrs%22...", ErrorSeverity.StepUpRequired, null)]
    [InlineData("ActiveDurationTooShort", "The Active duration is too short. Minimum Required is 5 minutes.", ErrorSeverity.Validation, null)]
    [InlineData("AuthorizationFailed", "The client does not have authorization to perform action", ErrorSeverity.Fatal, null)]
    [InlineData("RoleAssignmentExists", "The Role assignment already exists.", ErrorSeverity.Info, null)]
    [InlineData("InvalidRoleAssignmentRequestSchedule", "The role assignment request schedule is invalid.", ErrorSeverity.Validation, "duration")]
    public void Map_ArmError_ReturnsExpectedSeverityAndFieldHint(
        string code,
        string message,
        ErrorSeverity severity,
        string? fieldHint)
    {
        // ARM shares Graph's code vocabulary but folds every policy failure
        // into one code and names the rule only in the message.
        var mapped = PimErrorMapper.Map(new ArmRequestException(400, code, message));

        Assert.Equal(severity, mapped.Severity);
        Assert.Equal(fieldHint, mapped.FieldHint);
        Assert.NotEmpty(mapped.Message);
    }

    [Fact]
    public void Map_ArmThrottled_ReturnsThrottled()
    {
        var mapped = PimErrorMapper.Map(new ArmRequestException(429, string.Empty, string.Empty));

        Assert.Equal(ErrorSeverity.Throttled, mapped.Severity);
    }

    [Fact]
    public void MapException_ArmRequestException_DelegatesToCodeMapping()
    {
        var mapped = PimErrorMapper.MapException(
            new ArmRequestException(400, "RoleAssignmentExists", "The Role assignment already exists."));

        Assert.Equal(ErrorSeverity.Info, mapped.Severity);
    }

    [Fact]
    public void Map_UnknownCode_ReturnsFatalFallback()
    {
        var error = new ODataError { Error = new MainError { Code = "SomethingUnexpected" } };

        var mapped = PimErrorMapper.Map(error);

        Assert.Equal(ErrorSeverity.Fatal, mapped.Severity);
    }

    [Fact]
    public void Describe_StepUpRequiredOnDeviceCodeAccount_ExplainsTheSignInMethodCannotSatisfyIt()
    {
        var error = new UserFacingError(ErrorSeverity.StepUpRequired, "generic step-up message", null);

        var described = PimErrorMapper.Describe(error, AuthMethod.DeviceCode);

        Assert.Contains("device-code", described, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("standard sign-in", described, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("try again", described, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_StepUpRequiredOnBrokerAccount_KeepsTheOriginalMessage()
    {
        // The broker path CAN satisfy the challenge — retrying is sound advice there.
        var error = new UserFacingError(ErrorSeverity.StepUpRequired, "generic step-up message", null);

        Assert.Equal("generic step-up message", PimErrorMapper.Describe(error, AuthMethod.Broker));
    }

    [Fact]
    public void Describe_OtherSeverityOnDeviceCodeAccount_KeepsTheOriginalMessage()
    {
        var error = new UserFacingError(ErrorSeverity.Validation, "A justification is required.", "justification");

        Assert.Equal("A justification is required.", PimErrorMapper.Describe(error, AuthMethod.DeviceCode));
    }

    [Theory]
    [InlineData("StartTimeInPast", true)]
    [InlineData("InvalidStartDateTime", true)]
    [InlineData("JustificationRuleViolated", false)]
    public void IsStartTimeInPast_DetectsClockSkewCodes(string code, bool expected)
    {
        var error = new ODataError { Error = new MainError { Code = code } };

        Assert.Equal(expected, PimErrorMapper.IsStartTimeInPast(error));
    }

    [Fact]
    public void MapException_ODataError_DelegatesToCodeMapping()
    {
        var error = new ODataError { Error = new MainError { Code = "EligibilityNotFound" } };

        var mapped = PimErrorMapper.MapException(error);

        Assert.Equal(ErrorSeverity.RefreshList, mapped.Severity);
    }

    [Fact]
    public void MapException_OperationCanceled_ReturnsTimeout()
    {
        var mapped = PimErrorMapper.MapException(new OperationCanceledException());

        Assert.Equal(ErrorSeverity.Timeout, mapped.Severity);
        Assert.NotEmpty(mapped.Message);
    }

    [Fact]
    public void MapException_HttpRequestException_ReturnsOffline()
    {
        var mapped = PimErrorMapper.MapException(new HttpRequestException("connection refused"));

        Assert.Equal(ErrorSeverity.Offline, mapped.Severity);
    }

    [Fact]
    public void MapException_WrappedSocketException_ReturnsOffline()
    {
        var wrapped = new InvalidOperationException(
            "request failed",
            new System.Net.Sockets.SocketException());

        var mapped = PimErrorMapper.MapException(wrapped);

        Assert.Equal(ErrorSeverity.Offline, mapped.Severity);
    }

    [Fact]
    public void MapException_UnknownException_ReturnsFatalFallback()
    {
        var mapped = PimErrorMapper.MapException(new InvalidOperationException("boom"));

        Assert.Equal(ErrorSeverity.Fatal, mapped.Severity);
    }

    [Fact]
    public void MapException_MsalInvalidClient_ExplainsPublicClientFlowFix()
    {
        // AADSTS7000218: app registration missing "Allow public client flows".
        var msal = new MsalServiceException(
            MsalError.InvalidClient,
            "AADSTS7000218: The request body must contain the following parameter: 'client_assertion' or 'client_secret'.");

        var mapped = PimErrorMapper.MapException(msal);

        Assert.Equal(ErrorSeverity.Fatal, mapped.Severity);
        Assert.Contains("public client flows", mapped.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MapException_MsalInvalidClientByMessageOnly_ExplainsPublicClientFlowFix()
    {
        // Same fix path when the AADSTS code is only in the message, not the ErrorCode.
        var msal = new MsalServiceException(
            "some_other_code",
            "Original exception: AADSTS7000218: ...");

        var mapped = PimErrorMapper.MapException(msal);

        Assert.Contains("public client flows", mapped.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MapException_WamPromptCancelled_ReturnsFriendlyCancellation()
    {
        var mapped = PimErrorMapper.MapException(
            new MsalClientException(MsalError.AuthenticationCanceledError, "User canceled authentication."));

        Assert.Equal(ErrorSeverity.Info, mapped.Severity);
        Assert.Contains("cancelled", mapped.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MapException_WamPromptCancelledAsServiceException_ReturnsFriendlyCancellation()
    {
        // Proves the cancellation arm precedes the MsalServiceException arm.
        var mapped = PimErrorMapper.MapException(
            new MsalServiceException(MsalError.AuthenticationCanceledError, "User canceled authentication."));

        Assert.Contains("cancelled", mapped.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MapException_TenantMismatch_PointsAtTheEntry()
    {
        var msal = new MsalServiceException("tenant_mismatch", "detail for the log");

        var mapped = PimErrorMapper.MapException(msal);

        Assert.Equal(ErrorSeverity.Fatal, mapped.Severity);
        Assert.Contains("tenant id", mapped.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("detail for the log", mapped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MapException_AppRegistrationMissing_PointsAtSettings()
    {
        var msal = new MsalServiceException("app_registration_missing", "No App Registration is configured for tenant x in Entra Global.");

        var mapped = PimErrorMapper.MapException(msal);

        Assert.Equal(ErrorSeverity.Fatal, mapped.Severity);
        Assert.Contains("Settings → Tenants", mapped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MapException_Aadsts700016_PointsAtTheEntryAndConsent()
    {
        var msal = new MsalServiceException("unauthorized_client", "AADSTS700016: Application not found in the directory");

        var mapped = PimErrorMapper.MapException(msal);

        Assert.Contains("unknown in the selected tenant", mapped.Message, StringComparison.Ordinal);
        Assert.Contains("admin consent", mapped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MapException_MsalOtherServiceError_ReturnsGenericSignInFailure()
    {
        var msal = new MsalServiceException("some_error", "AADSTS50000: something else");

        var mapped = PimErrorMapper.MapException(msal);

        Assert.Equal(ErrorSeverity.Fatal, mapped.Severity);
        Assert.Contains("Sign-in failed", mapped.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("AadPremiumLicenseRequired", "some message")]
    [InlineData("BadRequest", "The tenant needs to have Microsoft Entra ID P2 or Microsoft Entra ID Governance license.")]
    public void DescribeFetchFailure_MissingPimLicence_NamesTheLicence(string code, string message)
    {
        var error = new ODataError { Error = new MainError { Code = code, Message = message } };

        var caption = PimErrorMapper.DescribeFetchFailure(error);

        Assert.Contains("P2 or Governance license", caption, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeFetchFailure_NoMsalAccountForEnrollment_TellsTheUserToReAddTheAccount()
    {
        // MsalAuthService throws this when the enrollment's registration changed
        // underneath it — the only repair is a fresh sign-in.
        var caption = PimErrorMapper.DescribeFetchFailure(
            new MsalUiRequiredException(MsalError.UserNullError, "No MSAL account for oid"));

        Assert.Contains("add it again", caption, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeFetchFailure_TenantHasNotConsented_NamesConsentAndSelfRecovery()
    {
        // AADSTS65001 is the window between a scope change and the admin's
        // consent — the account is not broken, it is waiting.
        var caption = PimErrorMapper.DescribeFetchFailure(
            new MsalUiRequiredException("invalid_grant", "AADSTS65001: The user or administrator has not consented to use the application"));

        Assert.Contains("consent", caption, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recovers on its own", caption, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeFetchFailure_ArmRejected_NamesResourceManager()
    {
        var caption = PimErrorMapper.DescribeFetchFailure(
            new ArmRequestException(403, "AuthorizationFailed", "The client does not have authorization"));

        Assert.Contains("Azure Resource Manager", caption, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeFetchFailure_CancelledDuringSignIn_NamesThePromptAndConsent()
    {
        // Field case 2026-09-04: a tenant without ARM consent produced "the request
        // timed out", which reads as a network fault and sends the admin the wrong way.
        var caption = PimErrorMapper.DescribeFetchFailure(
            new ArmSignInPendingException(new OperationCanceledException()));

        Assert.Contains("signing in", caption, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("consent", caption, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("timed out", caption, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescribeFetchFailure_DeviceStateCaOnDeviceCodeAccount_PointsAtTheStandardSignIn()
    {
        // Field case 2026-09-10: an ARM read on a device-code enrollment fails with
        // AADSTS53001 every hour forever, because that token path carries no device
        // claim. Refreshing can never help; only re-adding via the broker can.
        var caption = PimErrorMapper.DescribeFetchFailure(
            new MsalUiRequiredException("invalid_grant", "AADSTS53001: Device is not in required device state: domain_joined."),
            AuthMethod.DeviceCode);

        Assert.Contains("device-code sign-in cannot present", caption, StringComparison.Ordinal);
        Assert.Contains("standard sign-in", caption, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeFetchFailure_DeviceStateCaOnBrokerAccount_BlamesTheDeviceNotTheSignIn()
    {
        // Same policy, broker enrollment: re-adding the account lands in exactly the
        // same place, so the advice must not be "sign in differently".
        var caption = PimErrorMapper.DescribeFetchFailure(
            new MsalUiRequiredException("invalid_grant", "AADSTS53000: Device is not in required device state: compliant."),
            AuthMethod.Broker);

        Assert.Contains("requires a managed device", caption, StringComparison.Ordinal);
        Assert.DoesNotContain("add it again", caption, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("device-code", caption, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescribeFetchFailure_OtherFailure_StaysGeneric()
    {
        var caption = PimErrorMapper.DescribeFetchFailure(new InvalidOperationException("upstream blew up"));

        Assert.Contains("Couldn't load eligibilities", caption, StringComparison.Ordinal);
        Assert.DoesNotContain("upstream", caption, StringComparison.OrdinalIgnoreCase);
    }
}
