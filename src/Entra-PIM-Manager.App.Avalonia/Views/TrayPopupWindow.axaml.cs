namespace EntraPimManager.AppAvalonia.Views;

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using EntraPimManager.AppAvalonia.ViewModels;

/// <summary>
/// Tray popup. ESC and clicking outside the window hide it (the tray icon
/// stays alive). The window is created once and re-used.
/// </summary>
public partial class TrayPopupWindow : Window
{
    // Drag threshold: cheaper than reading PlatformSettings, and 4 CSS-px is
    // the de-facto convention across Win32 desktop apps.
    private const double DragThresholdPixels = 4.0;

    // In-process format — Avalonia 12 replaces the old DataObject/DoDragDrop
    // API with typed DataFormat<T>. The identifier is for diagnostics only;
    // type-equality is what the format matches by.
    private static readonly DataFormat<AccountListItemViewModel> AccountDragFormat =
        DataFormat.CreateInProcessFormat<AccountListItemViewModel>("EntraPimManager.AccountListItem");

    private AccountListItemViewModel? _potentialDragSource;
    private PointerPressedEventArgs? _potentialDragSourceEvent;
    private Point _dragStartPoint;

    public TrayPopupWindow()
    {
        InitializeComponent();
        Deactivated += OnDeactivated;
        KeyDown += OnKeyDown;
    }

    /// <summary>
    /// UTC timestamp of the most recent auto-hide caused by losing focus.
    /// <see cref="Tray.TrayPopupController.Toggle"/> consults this so a tray
    /// click that fires immediately after the deactivation-hide is treated
    /// as a "close" rather than reopening the popup right after closing it.
    /// </summary>
    public DateTimeOffset LastHiddenByDeactivation { get; private set; } = DateTimeOffset.MinValue;

    private void OnDeactivated(object? sender, EventArgs e)
    {
        LastHiddenByDeactivation = DateTimeOffset.UtcNow;
        Hide();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
        }
    }

    /// <summary>
    /// Records a potential drag source when the user presses on an account row.
    /// The actual drag isn't started until the pointer moves past
    /// <see cref="DragThresholdPixels"/> so a normal click (which selects the
    /// account) still works. We also stash the press event itself — Avalonia 12's
    /// <see cref="DragDrop.DoDragDropAsync"/> demands a
    /// <see cref="PointerPressedEventArgs"/> trigger.
    /// </summary>
    private void OnAccountRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is AccountListItemViewModel vm)
        {
            _potentialDragSource = vm;
            _potentialDragSourceEvent = e;
            _dragStartPoint = e.GetPosition(this);
        }
    }

    private async void OnAccountRowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_potentialDragSource is not { } source || _potentialDragSourceEvent is not { } triggerEvent)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ClearPotentialDrag();
            return;
        }

        var current = e.GetPosition(this);
        var dx = current.X - _dragStartPoint.X;
        var dy = current.Y - _dragStartPoint.Y;
        if (Math.Abs(dx) < DragThresholdPixels && Math.Abs(dy) < DragThresholdPixels)
        {
            return;
        }

        // Threshold crossed — promote to an actual drag. Clear the fields
        // first so a re-entrant pointer-move during DoDragDropAsync can't
        // start a second drag.
        ClearPotentialDrag();

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(AccountDragFormat, source));

        try
        {
            await DragDrop.DoDragDropAsync(triggerEvent, data, DragDropEffects.Move);
        }
        catch (Exception)
        {
            // DnD can be cancelled by the OS at any point; nothing to recover
            // here — the source data was never persisted.
        }
    }

    private void OnAccountRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ClearPotentialDrag();
    }

    private void OnAccountRowDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer is { } dt && dt.Contains(AccountDragFormat))
        {
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnAccountRowDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control target
            || target.DataContext is not AccountListItemViewModel targetVm
            || e.DataTransfer is not { } dt
            || dt.TryGetValue(AccountDragFormat) is not { } dragged
            || DataContext is not ShellViewModel shell)
        {
            return;
        }

        // Dropping onto itself is a no-op — otherwise Avalonia would still
        // treat it as a successful move and trigger a write to disk.
        if (ReferenceEquals(dragged, targetVm))
        {
            return;
        }

        var newIndex = shell.Accounts.IndexOf(targetVm);
        if (newIndex < 0)
        {
            return;
        }

        e.Handled = true;
        _ = shell.MoveAccountAsync(dragged.Account, newIndex);
    }

    private void ClearPotentialDrag()
    {
        _potentialDragSource = null;
        _potentialDragSourceEvent = null;
    }
}
