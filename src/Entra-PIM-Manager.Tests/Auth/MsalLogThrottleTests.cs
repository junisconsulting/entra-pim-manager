namespace EntraPimManager.Tests.Auth;

using EntraPimManager.Core.Auth;
using Microsoft.Extensions.Logging;

/// <summary>
/// The samples are real lines from a field log that came back at 6.5 MB for half a
/// day, 99.6 % of it these two warnings repeating on every token acquisition.
/// </summary>
public sealed class MsalLogThrottleTests
{
    private const string Authority =
        "False MSAL 4.66.0.0 MSAL.NetCore .NET 8.0.27 Microsoft Windows 10.0.26200 "
        + "[{0}] (Entra-PIM-Manager) [MSAL:{1}]\tWARNING\tSetAuthorityUri:78\t"
        + "Initializing authority from URI 'https://login.microsoftonline.com/{2}/' without authority type";

    [Fact]
    public void ShouldLog_TheSameWarningOnEveryCall_PassesOnlyTheFirst()
    {
        var throttle = new MsalLogThrottle();

        Assert.True(throttle.ShouldLog(LogLevel.Warning, Line("2026-09-05 22:00:51Z", "4042")));

        // A minute later, next refresh tick: same message, new timestamp and call id.
        Assert.False(throttle.ShouldLog(LogLevel.Warning, Line("2026-09-05 22:01:51Z", "4187")));
        Assert.False(throttle.ShouldLog(LogLevel.Warning, Line("2026-09-05 22:02:51Z", "4331")));
    }

    [Fact]
    public void ShouldLog_SameWarningForAnotherTenant_PassesAgain()
    {
        // The authority URI is part of what identifies the message, so a second
        // tenant is a second line. Collapsing those would hide which tenant is meant.
        var throttle = new MsalLogThrottle();
        var first = "11111111-1111-4111-8111-111111111111";
        var second = "22222222-2222-4222-8222-222222222222";

        Assert.True(throttle.ShouldLog(LogLevel.Warning, Line("2026-09-05 22:00:51Z", "4042", first)));
        Assert.True(throttle.ShouldLog(LogLevel.Warning, Line("2026-09-05 22:00:52Z", "4043", second)));
        Assert.False(throttle.ShouldLog(LogLevel.Warning, Line("2026-09-05 22:01:51Z", "4187", first)));
    }

    [Fact]
    public void ShouldLog_DifferentWarning_IsNotSwallowedByTheFirst()
    {
        var throttle = new MsalLogThrottle();
        const string other =
            "False MSAL 4.66.0.0 [2026-09-05 22:00:51Z] (Entra-PIM-Manager) [MSAL:4043]\tWARNING\t"
            + "TryEnqueueMsaDeviceCredentialAcquisitionAndContinue:1052\tMsaDeviceOperationProvider is not available.";

        Assert.True(throttle.ShouldLog(LogLevel.Warning, Line("2026-09-05 22:00:51Z", "4042")));
        Assert.True(throttle.ShouldLog(LogLevel.Warning, other));
    }

    [Fact]
    public void ShouldLog_Errors_AreNeverHeldBack()
    {
        // An error belongs to one operation a user is waiting on, not to MSAL's
        // description of the machine — the second occurrence matters as much as the first.
        var throttle = new MsalLogThrottle();
        const string failure = "[2026-09-05 22:00:51Z] [MSAL:9] ERROR Token acquisition failed";

        Assert.True(throttle.ShouldLog(LogLevel.Error, failure));
        Assert.True(throttle.ShouldLog(LogLevel.Error, failure));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ShouldLog_NothingToDeduplicate_PassesThrough(string? message)
        => Assert.True(new MsalLogThrottle().ShouldLog(LogLevel.Warning, message));

    [Fact]
    public void ShouldLog_FarMoreDistinctMessagesThanTheCeiling_KeepsWorking()
    {
        // The ceiling exists because MSAL puts correlation ids in some messages. Past
        // it the memory is dropped rather than grown — noisy for one round, bounded.
        var throttle = new MsalLogThrottle();
        for (var i = 0; i < 2000; i++)
        {
            Assert.True(throttle.ShouldLog(LogLevel.Warning, $"correlation {i} failed"));
        }

        Assert.True(throttle.ShouldLog(LogLevel.Warning, "correlation 1 failed"));
    }

    private static string Line(string timestamp, string callId, string tenant = "33333333-3333-4333-8333-333333333333")
        => string.Format(System.Globalization.CultureInfo.InvariantCulture, Authority, timestamp, callId, tenant);
}
