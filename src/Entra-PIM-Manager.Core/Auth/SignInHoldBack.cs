namespace EntraPimManager.Core.Auth;

/// <summary>
/// The switch behind <see cref="IAuthService.DeferInteractiveSignIn"/> and
/// <see cref="IAuthService.AllowInteractiveSignIn"/>: while deferred, a token request that
/// may open the WAM window runs silent-only, and one that needed the window is remembered.
/// </summary>
/// <remarks>
/// Kept out of <see cref="MsalAuthService"/>, which drives the live broker and cannot be
/// unit-tested, because a slip here opens an unprompted sign-in window at every logon.
/// Token requests are serialized by the auth service's lock; the switch is flipped from
/// the UI thread, hence the volatile fields.
/// </remarks>
internal sealed class SignInHoldBack
{
    private volatile bool _deferred;
    private volatile bool _heldBack;

    /// <summary>Holds the WAM window back from now until <see cref="Allow"/>.</summary>
    public void Defer() => _deferred = true;

    /// <summary>
    /// True when a request must run silent-only although its caller would allow the WAM
    /// window. A caller that asks for silent-only anyway is not held back, and not counted.
    /// </summary>
    public bool AppliesTo(bool silentOnly) => !silentOnly && _deferred;

    /// <summary>Records that a held-back request needed the user.</summary>
    public void RecordHeldBack() => _heldBack = true;

    /// <summary>Lets requests open the WAM window again.</summary>
    /// <returns>True, once, when a request was held back meanwhile.</returns>
    public bool Allow()
    {
        _deferred = false;
        var heldBack = _heldBack;
        _heldBack = false;
        return heldBack;
    }
}
