namespace EntraPimManager.AppAvalonia.Services;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using EntraPimManager.Core.Auth;

/// <summary>
/// Supplies a parent window handle for interactive WAM prompts. The Avalonia
/// equivalent of the WPF <c>WindowTracker</c>: it uses
/// <see cref="TopLevel.TryGetPlatformHandle"/> on the currently registered
/// popup window. Falls back to <c>GetForegroundWindow</c> if the popup is not
/// available — never returns <c>IntPtr.Zero</c>.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class AvaloniaWindowTracker : IWindowTracker
{
    private Window? _trackedWindow;

    /// <summary>Registers <paramref name="window"/> as the WAM prompt parent.</summary>
    public void Track(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _trackedWindow = window;
    }

    /// <inheritdoc />
    public nint GetCurrentWindowHandle()
    {
        if (_trackedWindow is not null)
        {
            var handle = _trackedWindow.TryGetPlatformHandle()?.Handle ?? nint.Zero;
            if (handle != nint.Zero)
            {
                return handle;
            }
        }

        var foreground = GetForegroundWindow();
        return foreground != nint.Zero ? foreground : nint.Zero;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();
}
