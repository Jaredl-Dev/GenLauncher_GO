using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using GenLauncherGO.UI.Features.Mods;

namespace GenLauncherGO.UI.Features.Launcher.Support;

/// <summary>
/// Owns pointer gestures for modification reordering and selected content-tile toggles in the main launcher lists.
/// </summary>
internal sealed class LauncherDragDropController
{
    private const double PointerMovementTolerance = 6;

    private Point _dragStartPoint;

    private ModificationViewModel? _draggedModification;

    private IPointer? _capturedPointer;

    private ModificationViewModel? _selectionToggleCandidate;

    private ListBox? _selectionToggleOwner;

    private Visual? _selectionToggleCoordinateSpace;

    private Point _selectionToggleStart;

    public bool IsDragging { get; private set; }

    /// <summary>
    /// Captures a possible content-selection toggle and, when allowed, modification reorder gesture.
    /// </summary>
    public void CapturePointerGesture(
        ListBox contentList,
        Visual selectionCoordinateSpace,
        PointerPressedEventArgs eventArgs,
        bool canReorder)
    {
        ArgumentNullException.ThrowIfNull(contentList);
        ArgumentNullException.ThrowIfNull(selectionCoordinateSpace);
        ArgumentNullException.ThrowIfNull(eventArgs);

        CancelPointerGesture();

        PointerPoint point = eventArgs.GetCurrentPoint(contentList);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        ModificationViewModel? modification = ResolveContentTile(
            contentList,
            eventArgs.Source as Visual,
            rejectInteractiveChild: true);
        if (modification == null)
        {
            return;
        }

        if (canReorder)
        {
            _dragStartPoint = point.Position;
            _draggedModification = modification;
        }

        if (contentList.SelectionMode == SelectionMode.Single && modification.IsSelected)
        {
            _selectionToggleCandidate = modification;
            _selectionToggleOwner = contentList;
            _selectionToggleCoordinateSpace = selectionCoordinateSpace;
            _selectionToggleStart = eventArgs.GetPosition(selectionCoordinateSpace);
        }
    }

    /// <summary>
    /// Promotes a captured pointer gesture to a reorder operation once it crosses the drag threshold.
    /// </summary>
    public bool HandlePointerMove(ListBox modsList, PointerEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(modsList);
        ArgumentNullException.ThrowIfNull(eventArgs);

        if (_draggedModification == null)
        {
            return false;
        }

        PointerPoint point = eventArgs.GetCurrentPoint(modsList);
        if (!point.Properties.IsLeftButtonPressed)
        {
            CancelPointerGesture();
            return false;
        }

        if (!IsDragging &&
            Math.Abs(point.Position.X - _dragStartPoint.X) <= PointerMovementTolerance &&
            Math.Abs(point.Position.Y - _dragStartPoint.Y) <= PointerMovementTolerance)
        {
            return false;
        }

        if (!IsDragging)
        {
            CancelSelectionToggle();
            IsDragging = true;
            _draggedModification.SetDragAndDropMod();
            _capturedPointer = eventArgs.Pointer;
            _capturedPointer.Capture(modsList);
            if (!_draggedModification.IsSelected)
            {
                modsList.SelectedItem = _draggedModification;
            }
        }

        ScrollDuringDrag(modsList, point.Position.Y);
        eventArgs.Handled = true;
        return true;
    }

    /// <summary>
    /// Completes the current pointer gesture, returning a valid reorder and any selected tile eligible for toggling.
    /// </summary>
    public bool TryCompletePointerGesture(
        ListBox contentList,
        PointerReleasedEventArgs eventArgs,
        out ModificationViewModel? selectionToggleCandidate,
        out int sourceIndex,
        out int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(contentList);
        ArgumentNullException.ThrowIfNull(eventArgs);

        selectionToggleCandidate = null;
        sourceIndex = -1;
        targetIndex = -1;

        ModificationViewModel? draggedModification = _draggedModification;
        bool wasDragging = IsDragging;
        ModificationViewModel? capturedSelection = _selectionToggleCandidate;
        ListBox? capturedSelectionOwner = _selectionToggleOwner;
        Visual? selectionCoordinateSpace = _selectionToggleCoordinateSpace;
        Point selectionStart = _selectionToggleStart;
        Point selectionEnd = selectionCoordinateSpace == null
            ? default
            : eventArgs.GetPosition(selectionCoordinateSpace);
        var hit = contentList.InputHitTest(eventArgs.GetPosition(contentList)) as Visual;
        ModificationViewModel? releasedModification = ResolveContentTile(
            contentList,
            hit,
            rejectInteractiveChild: false);

        CancelPointerGesture();

        if (!wasDragging &&
            capturedSelection != null &&
            ReferenceEquals(capturedSelectionOwner, contentList) &&
            ReferenceEquals(capturedSelection, releasedModification) &&
            selectionCoordinateSpace != null)
        {
            if (Math.Abs(selectionEnd.X - selectionStart.X) <= PointerMovementTolerance &&
                Math.Abs(selectionEnd.Y - selectionStart.Y) <= PointerMovementTolerance)
            {
                selectionToggleCandidate = capturedSelection;
            }
        }

        if (!wasDragging ||
            draggedModification == null ||
            releasedModification == null)
        {
            return false;
        }

        sourceIndex = contentList.Items.IndexOf(draggedModification);
        targetIndex = contentList.Items.IndexOf(releasedModification);
        return sourceIndex >= 0 && targetIndex >= 0 && sourceIndex != targetIndex;
    }

    /// <summary>
    /// Clears any active pointer gesture state.
    /// </summary>
    public void CancelPointerGesture()
    {
        _capturedPointer?.Capture(null);
        _capturedPointer = null;
        _draggedModification?.RemoveDragAndDropMod();
        _draggedModification = null;
        IsDragging = false;
        CancelSelectionToggle();
    }

    /// <summary>
    /// Scrolls the list when a reorder pointer approaches its top or bottom edge.
    /// </summary>
    public static void ScrollDuringDrag(ListBox modsList, double verticalPosition)
    {
        ArgumentNullException.ThrowIfNull(modsList);

        ScrollViewer? scrollViewer = modsList
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();
        if (scrollViewer == null)
        {
            return;
        }

        const double Tolerance = 40;
        const double Offset = 15;
        double requestedOffset = scrollViewer.Offset.Y;

        if (verticalPosition < Tolerance)
        {
            requestedOffset -= Offset;
        }
        else if (verticalPosition > modsList.Bounds.Height - Tolerance)
        {
            requestedOffset += Offset;
        }
        else
        {
            return;
        }

        double maximumOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        scrollViewer.Offset = scrollViewer.Offset.WithY(
            Math.Clamp(requestedOffset, 0, maximumOffset));
    }

    private void CancelSelectionToggle()
    {
        _selectionToggleCandidate = null;
        _selectionToggleOwner = null;
        _selectionToggleCoordinateSpace = null;
    }

    private static ModificationViewModel? ResolveContentTile(
        ListBox expectedOwner,
        Visual? source,
        bool rejectInteractiveChild)
    {
        for (Visual? current = source; current != null; current = current.GetVisualParent())
        {
            if (rejectInteractiveChild && current is Button or ComboBox or MenuItem)
            {
                return null;
            }

            if (current is not ListBoxItem item)
            {
                continue;
            }

            return ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), expectedOwner)
                ? item.DataContext as ModificationViewModel
                : null;
        }

        return null;
    }
}
