namespace EntraPimManager.AppAvalonia.ViewModels;

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

/// <summary>
/// Two-step confirmation for a destructive button: the first press arms it, a second press
/// within <see cref="ArmedFor"/> runs the action, and anything else disarms it again.
/// </summary>
/// <remarks>
/// Stands in for a confirmation dialog, which this app has nowhere else. The armed state
/// times out on purpose: a button left armed indefinitely would let a stray click much later
/// remove what the user had long stopped meaning to remove.
/// <para/>
/// The view binds one button to <see cref="Command"/> and switches only its look on
/// <see cref="IsArmed"/>. Swapping in a second button would hide the one that holds the
/// keyboard focus, and Escape would then reach the window instead of the editor.
/// </remarks>
public sealed partial class ArmedConfirmation : ObservableObject
{
    private static readonly TimeSpan ArmedFor = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How soon after the previous press another one still counts as the same gesture.
    /// Windows' default double-click time. Each ignored press restarts the wait, so neither a
    /// double-click nor a held Enter key — whose auto-repeat arrives ~33 ms apart — can arm and
    /// confirm in one go, the one-gesture loss this class exists to prevent.
    /// </summary>
    private static readonly TimeSpan SameGesture = TimeSpan.FromMilliseconds(500);

    private readonly Func<Task> _confirmed;
    private readonly Func<bool> _canRun;
    private DispatcherTimer? _timer;
    private long _lastPressMilliseconds;

    /// <summary>True between the first press and the one that confirms.</summary>
    [ObservableProperty]
    private bool _isArmed;

    /// <summary>Creates the confirmation for one destructive action.</summary>
    /// <param name="confirmed">The action the second press runs.</param>
    /// <param name="canRun">Whether the button may be pressed at all right now.</param>
    public ArmedConfirmation(Func<Task> confirmed, Func<bool> canRun)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        ArgumentNullException.ThrowIfNull(canRun);
        _confirmed = confirmed;
        _canRun = canRun;
        Command = new AsyncRelayCommand(PressAsync, canRun);
    }

    /// <summary>The button's command: the first press arms, the second runs the action.</summary>
    public IAsyncRelayCommand Command { get; }

    /// <summary>Back to the unarmed state. Safe to call when not armed.</summary>
    public void Disarm()
    {
        _timer?.Stop();
        IsArmed = false;
    }

    /// <summary>
    /// Re-evaluates the button against the can-run check, which changes without anything
    /// here being touched — the shell's busy flag. Disarms when the action can no longer
    /// run: an armed button must not outlast the moment it was armed in.
    /// </summary>
    public void Refresh()
    {
        if (!_canRun())
        {
            Disarm();
        }

        Command.NotifyCanExecuteChanged();
    }

    private async Task PressAsync()
    {
        var now = Environment.TickCount64;
        if (IsArmed)
        {
            if (now - _lastPressMilliseconds < (long)SameGesture.TotalMilliseconds)
            {
                _lastPressMilliseconds = now;
                return;
            }

            Disarm();
            await _confirmed();
            return;
        }

        // Created on first use rather than in the constructor: presses come from the command,
        // so this is the UI thread the timer has to tick on.
        _timer ??= CreateTimer();
        _lastPressMilliseconds = now;
        IsArmed = true;
        _timer.Start();
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer { Interval = ArmedFor };
        timer.Tick += (_, _) => Disarm();
        return timer;
    }
}
