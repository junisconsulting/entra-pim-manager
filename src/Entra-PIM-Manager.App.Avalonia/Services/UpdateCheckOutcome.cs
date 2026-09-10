namespace EntraPimManager.AppAvalonia.Services;

/// <summary>
/// What an update check concluded. Exists because "no update to offer" has four
/// causes that a user pressing "Check now" must be able to tell apart: reporting
/// "you are up to date" while GitHub was unreachable — or while a release is
/// merely being held back by the age gate — is a lie the user acts on.
/// </summary>
public enum UpdateCheckOutcome
{
    /// <summary>A newer release is available and offerable right now.</summary>
    UpdateAvailable,

    /// <summary>The running version is the newest published one.</summary>
    UpToDate,

    /// <summary>
    /// A newer release exists but is younger than the reputation window an
    /// unsigned build needs; see <see cref="UpdateService"/>. It becomes
    /// offerable on its own once old enough.
    /// </summary>
    Deferred,

    /// <summary>The feed or the GitHub API could not be read.</summary>
    Failed,

    /// <summary>Not running from a Velopack install — there is nothing to update.</summary>
    NotSupported,
}
