namespace EntraPimManager.AppAvalonia.Services;

/// <summary>
/// Manages the per-user Start menu shortcut Velopack creates at install time —
/// letting the user opt out of it (and back in) at runtime. Backed by Velopack's
/// own shortcut machinery so the entry is identical to the installer's, including
/// the AppUserModelId the toast notifications depend on.
/// </summary>
public interface IShortcutService
{
    /// <summary>
    /// True only when running from a real Velopack install. In dev / portable
    /// launches there is no install root to anchor a shortcut to, so the other
    /// members no-op and the Settings toggle hides.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>Whether a Start menu shortcut for this app currently exists.</summary>
    bool IsStartMenuShortcutPresent { get; }

    /// <summary>Creates the Start menu shortcut (idempotent — no-op if already present).</summary>
    void EnableStartMenuShortcut();

    /// <summary>Removes the Start menu shortcut (idempotent — no-op if already absent).</summary>
    void DisableStartMenuShortcut();
}
