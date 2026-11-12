namespace EntraPimManager.Tests.Auth;

using EntraPimManager.Core.Auth;

/// <summary>
/// The switch that keeps the startup load from opening a sign-in window nobody asked for
/// (#20). A slip here either opens WAM at every logon on a pooled host, or loses the
/// sign-in the first panel open is supposed to repeat.
/// </summary>
public sealed class SignInHoldBackTests
{
    [Fact]
    public void AppliesTo_BeforeDefer_HoldsNothingBack()
    {
        var holdBack = new SignInHoldBack();

        Assert.False(holdBack.AppliesTo(silentOnly: false));
    }

    [Fact]
    public void AppliesTo_Deferred_HoldsBackOnlyRequestsThatMayOpenAWindow()
    {
        var holdBack = new SignInHoldBack();
        holdBack.Defer();

        Assert.True(holdBack.AppliesTo(silentOnly: false));
        Assert.False(holdBack.AppliesTo(silentOnly: true));
    }

    [Fact]
    public void Allow_AfterAHeldBackRequest_ReportsItOnceAndHoldsNothingBackAnyMore()
    {
        var holdBack = new SignInHoldBack();
        holdBack.Defer();
        holdBack.RecordHeldBack();

        Assert.True(holdBack.Allow());
        Assert.False(holdBack.Allow());
        Assert.False(holdBack.AppliesTo(silentOnly: false));
    }

    [Fact]
    public void Allow_WithoutAHeldBackRequest_ReportsNothing()
    {
        var holdBack = new SignInHoldBack();
        holdBack.Defer();

        Assert.False(holdBack.Allow());
    }
}
