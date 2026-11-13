namespace EntraPimManager.Tests.Auth;

using System.Diagnostics;
using EntraPimManager.Core.Auth;
using Microsoft.Extensions.Logging;
using Moq;

/// <summary>
/// The MSAL cache extension reports an unreadable cache file only through its TraceSource
/// (issue #13: the cache came up empty after every logon, and the log said nothing).
/// The tests go through a real TraceSource, the way the extension calls it.
/// </summary>
public sealed class MsalCacheTraceListenerTests
{
    // Real lines from the field test of 2026-11-13, one minute apart.
    private const string FirstRead = "[MSAL.Extension][2026-11-13T06:16:47.9771966Z] Could not read the token cache. Ignoring. See previous error message.";
    private const string NextRead = "[MSAL.Extension][2026-11-13T06:17:48.0563034Z] Could not read the token cache. Ignoring. See previous error message.";

    [Theory]
    [InlineData(TraceEventType.Critical, LogLevel.Warning)]
    [InlineData(TraceEventType.Error, LogLevel.Warning)]
    [InlineData(TraceEventType.Warning, LogLevel.Warning)]
    [InlineData(TraceEventType.Information, LogLevel.Debug)]
    [InlineData(TraceEventType.Verbose, LogLevel.Debug)]
    public void TraceEvent_ReachesTheLogAtTheMappedLevel(TraceEventType eventType, LogLevel expected)
    {
        var logger = EnabledLogger();

        Source(logger.Object).TraceEvent(eventType, 0, FirstRead);

        VerifyLogged(logger, expected, Times.Once());
    }

    [Fact]
    public void TraceEvent_TheSameWarningOnEveryRefresh_IsLoggedOnce()
    {
        // While the file stays unreadable the extension repeats this on every refresh,
        // about twelve lines a minute; only the timestamp differs.
        var logger = EnabledLogger();
        var source = Source(logger.Object);

        source.TraceEvent(TraceEventType.Warning, 0, FirstRead);
        source.TraceEvent(TraceEventType.Warning, 0, NextRead);

        VerifyLogged(logger, LogLevel.Warning, Times.Once());
    }

    private static Mock<ILogger> EnabledLogger()
    {
        var logger = new Mock<ILogger>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        return logger;
    }

    private static TraceSource Source(ILogger logger)
    {
        var source = new TraceSource("MsalCacheTest", SourceLevels.All);
        source.Listeners.Clear();
        source.Listeners.Add(new MsalCacheTraceListener(logger));
        return source;
    }

    private static void VerifyLogged(Mock<ILogger> logger, LogLevel level, Times times)
        => logger.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("Could not read the token cache", StringComparison.Ordinal)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
}
