namespace EntraPimManager.Tests.ErrorHandling;

using EntraPimManager.Core.ErrorHandling;
using EntraPimManager.Core.Models;
using Microsoft.Graph.Models.ODataErrors;

public sealed class PimErrorMapperTests
{
    [Theory]
    [InlineData("JustificationRuleViolated", ErrorSeverity.Validation, "justification")]
    [InlineData("TicketingRuleViolated", ErrorSeverity.Validation, "ticket")]
    [InlineData("MaximumDurationExceeded", ErrorSeverity.Validation, "duration")]
    [InlineData("MfaRuleViolated", ErrorSeverity.StepUpRequired, null)]
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
}
