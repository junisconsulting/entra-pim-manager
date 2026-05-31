namespace EntraPimManager.Tests.Services;

using EntraPimManager.Core.Models;
using EntraPimManager.Core.Services;

public sealed class ActivationStatusParserTests
{
    [Theory]
    [InlineData("Provisioned", ActivationStatus.Provisioned)]
    [InlineData("Granted", ActivationStatus.Granted)]
    [InlineData("PendingApproval", ActivationStatus.PendingApproval)]
    [InlineData("PendingScheduleCreation", ActivationStatus.PendingScheduleCreation)]
    [InlineData("Denied", ActivationStatus.Denied)]
    [InlineData("Failed", ActivationStatus.Failed)]
    [InlineData("Revoked", ActivationStatus.Revoked)]
    public void Parse_KnownStatus_ReturnsMatchingEnum(string status, ActivationStatus expected) =>
        Assert.Equal(expected, ActivationStatusParser.Parse(status));

    [Theory]
    [InlineData("somethingUnexpected")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_UnknownOrMissingStatus_ReturnsUnknown(string? status) =>
        Assert.Equal(ActivationStatus.Unknown, ActivationStatusParser.Parse(status));
}
