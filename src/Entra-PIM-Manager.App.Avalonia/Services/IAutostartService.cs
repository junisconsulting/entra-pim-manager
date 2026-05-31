namespace EntraPimManager.AppAvalonia.Services;

/// <summary>
/// Manages the per-user autostart entry. Uses the <c>HKCU</c> Run key only —
/// never <c>HKLM</c>, never a scheduled task.
/// </summary>
public interface IAutostartService
{
    /// <summary>Whether Entra PIM Manager is registered to start with Windows.</summary>
    bool IsEnabled { get; }

    /// <summary>Registers Entra PIM Manager to start with Windows for the current user.</summary>
    void Enable();

    /// <summary>Removes the autostart registration.</summary>
    void Disable();
}
