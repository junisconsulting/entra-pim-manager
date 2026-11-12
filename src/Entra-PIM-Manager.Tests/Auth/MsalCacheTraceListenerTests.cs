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
    [Theory]
    [InlineData(TraceEventType.Critical, LogLevel.Warning)]
    [InlineData(TraceEventType.Error, LogLevel.Warning)]
    [InlineData(TraceEventType.Warning, LogLevel.Warning)]
    [InlineData(TraceEventType.Information, LogLevel.Debug)]
    [InlineData(TraceEventType.Verbose, LogLevel.Debug)]
    public void TraceEvent_ReachesTheLogAtTheMappedLevel(TraceEventType eventType, LogLevel expected)
    {
        var logger = new Mock<ILogger>();
        var source = new TraceSource("MsalCacheTest", SourceLevels.All);
        source.Listeners.Clear();
        source.Listeners.Add(new MsalCacheTraceListener(logger.Object));

        source.TraceEvent(eventType, 0, "Could not read the token cache. Ignoring. See previous error message.");

        logger.Verify(
            l => l.Log(
                expected,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("Could not read the token cache", StringComparison.Ordinal)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
