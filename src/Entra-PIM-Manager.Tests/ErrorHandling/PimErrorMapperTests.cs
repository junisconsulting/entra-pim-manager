namespace EntraPimManager.Tests.ErrorHandling;

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
    public void MapException_Aadsts50194_PointsAtTheTenantSpecificList()
    {
        // A single-tenant app pasted into a cloud row is sent to /organizations.
        var msal = new MsalServiceException(
            "invalid_request",
            "AADSTS50194: Application 'x' is not configured as a multi-tenant application.");

        var mapped = PimErrorMapper.MapException(msal);

        Assert.Equal(ErrorSeverity.Fatal, mapped.Severity);
        Assert.Contains("Tenant-specific registrations", mapped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MapException_RegistrationMismatch_TellsTheUserToPickTheTenantTarget()
    {
        var msal = new MsalServiceException("registration_mismatch", "detail for the log");

        var mapped = PimErrorMapper.MapException(msal);

        Assert.Equal(ErrorSeverity.Fatal, mapped.Severity);
        Assert.Contains("Sign in with", mapped.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("detail for the log", mapped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MapException_Aadsts700016_MentionsTheTenantAsWellAsTheCloud()
    {
        var msal = new MsalServiceException("unauthorized_client", "AADSTS700016: Application not found in the directory");

        var mapped = PimErrorMapper.MapException(msal);

        Assert.Contains("cloud or tenant", mapped.Message, StringComparison.Ordinal);
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
    public void DescribeFetchFailure_OtherFailure_StaysGeneric()
    {
        var caption = PimErrorMapper.DescribeFetchFailure(new InvalidOperationException("upstream blew up"));

        Assert.Contains("Couldn't load eligibilities", caption, StringComparison.Ordinal);
        Assert.DoesNotContain("upstream", caption, StringComparison.OrdinalIgnoreCase);
    }
}
