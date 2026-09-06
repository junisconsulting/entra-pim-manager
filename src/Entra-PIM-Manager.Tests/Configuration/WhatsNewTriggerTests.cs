namespace EntraPimManager.Tests.Configuration;

using EntraPimManager.Core.Configuration;

/// <summary>
/// Covers the three ways a version reaches a machine. The case that went wrong in
/// the field was the third: installing 0.8.0 over 0.9.0 to reproduce something and
/// then installing 0.9.0 again showed no notes, because the recorded version had
/// never changed — settings live outside the install directory and survive both.
/// </summary>
public sealed class WhatsNewTriggerTests
{
    [Fact]
    public void ShouldShow_AutomaticUpdate_NoMarkerButANewVersion_IsTrue()
    {
        // Velopack restarts the app itself; the version string is the only trace.
        Assert.True(WhatsNewTrigger.ShouldShow("0.9.0", "0.8.0", afterInstall: false));
    }

    [Fact]
    public void ShouldShow_FreshInstall_NothingRecordedYet_IsTrue()
    {
        Assert.True(WhatsNewTrigger.ShouldShow("0.9.0", null, afterInstall: true));
    }

    [Fact]
    public void ShouldShow_ReinstallOfAVersionAlreadySeen_IsTrue()
    {
        // The regression: same version, already recorded — but an installer just ran,
        // so this is an update from the user's point of view.
        Assert.True(WhatsNewTrigger.ShouldShow("0.9.0", "0.9.0", afterInstall: true));
    }

    [Fact]
    public void ShouldShow_OrdinaryRestart_IsFalse()
    {
        // The whole point of recording the version: it shows once, not every start.
        Assert.False(WhatsNewTrigger.ShouldShow("0.9.0", "0.9.0", afterInstall: false));
    }
}
