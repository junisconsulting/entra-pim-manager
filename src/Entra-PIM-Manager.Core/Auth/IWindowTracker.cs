namespace EntraPimManager.Core.Auth;

/// <summary>
/// Supplies a window handle that interactive WAM prompts are parented to.
/// A tray app frequently has no foreground window; the App-layer implementation
/// is responsible for always returning a usable handle — never <c>0</c>.
/// </summary>
public interface IWindowTracker
{
    /// <summary>Returns the native window handle to parent the WAM prompt on.</summary>
    nint GetCurrentWindowHandle();
}
