namespace EntraPimManager.AppAvalonia.Views;

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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
    private static readonly DataFormat<TenantNodeViewModel> TenantDragFormat =
        DataFormat.CreateInProcessFormat<TenantNodeViewModel>("EntraPimManager.TenantNode");

    private TenantNodeViewModel? _potentialDragSource;
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
    /// Records a potential drag source when the user presses a tenant card's handle.
    /// The actual drag isn't started until the pointer moves past
    /// <see cref="DragThresholdPixels"/> so a normal click still works. We also stash
    /// the press event itself — Avalonia 12's <see cref="DragDrop.DoDragDropAsync"/>
    /// demands a <see cref="PointerPressedEventArgs"/> trigger.
    /// </summary>
    private void OnTenantRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is TenantNodeViewModel vm)
        {
            _potentialDragSource = vm;
            _potentialDragSourceEvent = e;
            _dragStartPoint = e.GetPosition(this);
        }
    }

    private async void OnTenantRowPointerMoved(object? sender, PointerEventArgs e)
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
        data.Add(DataTransferItem.Create(TenantDragFormat, source));

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

    private void OnTenantRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ClearPotentialDrag();
    }

    /// <summary>
    /// Opens the alias editor on an account row and puts the caret in it.
    /// </summary>
    /// <remarks>
    /// The focus call is posted: the text box is collapsed at the moment of the
    /// click and only becomes focusable once the IsVisible change has laid out.
    /// </remarks>
    private void OnRenameAccountClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not AccountListItemViewModel vm)
        {
            return;
        }

        vm.BeginRenameCommand.Execute(null);

        // The editor is a sibling of this button inside the row grid.
        var box = (control.Parent as Grid)?.Children.OfType<TextBox>().FirstOrDefault();
        Dispatcher.UIThread.Post(
            () =>
            {
                box?.Focus();
                box?.SelectAll();
            },
            DispatcherPriority.Input);
    }

    /// <summary>
    /// Enter commits the alias, Escape discards it. Both mark the event handled —
    /// otherwise Escape bubbles to <see cref="OnKeyDown"/> and hides the whole
    /// popup in the middle of a rename.
    /// </summary>
    private void OnAliasBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not AccountListItemViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            vm.CommitRenameCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelRenameCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnTenantRowDragOver(object? sender, DragEventArgs e)
    {
        // A tenant nobody is signed into contributes no accounts, so it has no place
        // in the flat order the drop rewrites — refuse it before the drop rather than
        // letting the card snap back.
        var droppable = sender is Control { DataContext: TenantNodeViewModel { HasAccounts: true } };
        if (droppable && e.DataTransfer is { } dt && dt.Contains(TenantDragFormat))
        {
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnTenantRowDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control target
            || target.DataContext is not TenantNodeViewModel targetVm
            || e.DataTransfer is not { } dt
            || dt.TryGetValue(TenantDragFormat) is not { } dragged
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

        e.Handled = true;
        _ = shell.MoveTenantAsync(dragged, targetVm);
    }

    private void ClearPotentialDrag()
    {
        _potentialDragSource = null;
        _potentialDragSourceEvent = null;
    }
}
