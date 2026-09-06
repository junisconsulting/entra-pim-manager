namespace EntraPimManager.Core.Configuration;

/// <summary>
/// Decides whether the what's-new window is due on this launch.
/// </summary>
/// <remarks>
/// Two independent signals, because neither one covers the other:
/// <list type="bullet">
/// <item>The <b>version changed</b> since the last launch. This is what an automatic
/// update leaves behind — Velopack restarts the app with no marker of any kind, so
/// the version string is the only trace that anything happened.</item>
/// <item>This launch is the <b>first after an installer run</b>. Velopack raises its
/// OnFirstRun hook for a Setup.exe over an existing install too, not just for a fresh
/// one — and that is the only signal when the installed version is one this machine
/// has already recorded: a reinstall, or a downgrade and back.</item>
/// </list>
/// The second case is why this is not simply a string comparison. Someone who
/// installs an older build to reproduce something and then installs the current one
/// again has genuinely just updated, and the notes belong on screen — but their
/// recorded version never changed.
/// <para/>
/// This lives in Core rather than next to the controller so it can be tested: the
/// rule is three install paths wide, and the one it used to get wrong was invisible
/// from the outside.
/// </remarks>
public static class WhatsNewTrigger
{
    /// <summary>Whether to show the release notes now.</summary>
    /// <param name="currentVersion">Running version. The caller resolves it first — a
    /// launch that cannot read its own version has nothing to look the notes up by and
    /// never gets here, which is why this is not nullable.</param>
    /// <param name="lastSeenVersion">Version recorded on the last launch that showed the notes.</param>
    /// <param name="afterInstall">Whether an installer ran and this is the first launch since.</param>
    public static bool ShouldShow(string currentVersion, string? lastSeenVersion, bool afterInstall)
        => afterInstall || !string.Equals(lastSeenVersion, currentVersion, StringComparison.Ordinal);
}
