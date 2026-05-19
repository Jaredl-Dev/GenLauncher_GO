using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using GenLauncherGO.UI.Features.Mods;

namespace GenLauncherGO.UI.Features.Launcher.Support;

/// <summary>
///     Owns pointer gestures for modification reordering and selected content-tile toggles in the main launcher lists.
/// </summary>
internal sealed class LauncherDragDropController
{
    private const double PointerMovementTolerance = 6;

    private IPointer? _capturedPointer;

    private Point _dragStartPoint;

    private ModificationViewModel? _draggedModification;

    private ModificationViewModel? _selectionToggleCandidate;

    private Visual? _selectionToggleCoordinateSpace;

    private ListBox? _selectionToggleOwner;

    private Point _selectionToggleStart;

    public bool IsDragging { get; private set; }

    /// <summary>
    ///     Gets the tile currently being presented as a reorder operation.
    /// </summary>
    public ModificationViewModel? DraggedModification =>
        IsDragging ? _draggedModification : null;

    /// <summary>
    ///     Captures a possible content-selection toggle and, when allowed, modification reorder gesture.
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
            true);
        if (modification == null)
        {
            return;
        }

        if (canReorder)
        {
            _dragStartPoint = point.Position;
            _draggedModification = modification;
        }

        if (contentList.SelectionMode == SelectionMode.Single)
        {
            _selectionToggleCandidate = modification;
            _selectionToggleOwner = contentList;
            _selectionToggleCoordinateSpace = selectionCoordinateSpace;
            _selectionToggleStart = eventArgs.GetPosition(selectionCoordinateSpace);
        }

        // ListBox normally selects on press. Delay that semantic change until release so a reorder never
        // starts asynchronous content and theme work while this controller owns the pointer.
        _capturedPointer = eventArgs.Pointer;
        _capturedPointer.Capture(contentList);
        eventArgs.Handled = true;
    }

    /// <summary>
    ///     Promotes a captured pointer gesture to a reorder operation once it crosses the drag threshold.
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
        }

        ScrollDuringDrag(modsList, point.Position.Y);
        eventArgs.Handled = true;
        return true;
    }

    /// <summary>
    ///     Completes the current pointer gesture, returning a valid reorder and clearing a clicked selected tile.
    /// </summary>
    public bool TryCompletePointerGesture(
        ListBox contentList,
        PointerReleasedEventArgs eventArgs,
        out bool selectionCleared,
        out int sourceIndex,
        out int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(contentList);
        ArgumentNullException.ThrowIfNull(eventArgs);

        selectionCleared = false;
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
            false);

        if (capturedSelectionOwner != null)
        {
            eventArgs.Handled = true;
        }

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
                if (capturedSelection.IsSelected)
                {
                    capturedSelection.IsSelected = false;
                    if (ReferenceEquals(contentList.SelectedItem, capturedSelection))
                    {
                        contentList.SelectedItem = null;
                    }

                    selectionCleared = true;
                }
                else
                {
                    contentList.SelectedItem = capturedSelection;
                }
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
    ///     Clears any active pointer gesture state.
    /// </summary>
    public void CancelPointerGesture()
    {
        IPointer? capturedPointer = _capturedPointer;
        ModificationViewModel? draggedModification = _draggedModification;
        _capturedPointer = null;
        _draggedModification = null;
        IsDragging = false;
        CancelSelectionToggle();
        draggedModification?.RemoveDragAndDropMod();
        capturedPointer?.Capture(null);
    }

    /// <summary>
    ///     Scrolls the list when a reorder pointer approaches its top or bottom edge.
    /// </summary>
    public static bool ScrollDuringDrag(ListBox modsList, double verticalPosition)
    {
        ArgumentNullException.ThrowIfNull(modsList);

        ScrollViewer? scrollViewer = GetScrollViewer(modsList);
        if (scrollViewer == null)
        {
            return false;
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
            return false;
        }

        double maximumOffset = GetMaximumVerticalOffset(scrollViewer);
        double clampedOffset = Math.Clamp(requestedOffset, 0, maximumOffset);
        if (Math.Abs(clampedOffset - scrollViewer.Offset.Y) < 0.5)
        {
            return false;
        }

        scrollViewer.Offset = scrollViewer.Offset.WithY(clampedOffset);
        return true;
    }

    /// <summary>
    ///     Gets the main list's current vertical offset, treating an empty list as being at the top.
    /// </summary>
    public static double GetVerticalScrollOffset(ListBox modsList)
    {
        ArgumentNullException.ThrowIfNull(modsList);

        if (modsList.Items.Count == 0)
        {
            return 0;
        }

        return GetScrollViewer(modsList)?.Offset.Y ?? 0;
    }

    /// <summary>
    ///     Restores a main-list offset after layout, clamped to the content that still exists.
    /// </summary>
    public static void RestoreVerticalScrollOffset(ListBox modsList, double verticalOffset)
    {
        ArgumentNullException.ThrowIfNull(modsList);

        ScrollViewer? scrollViewer = GetScrollViewer(modsList);
        if (scrollViewer == null)
        {
            return;
        }

        double requestedOffset = modsList.Items.Count == 0 || !double.IsFinite(verticalOffset)
            ? 0
            : Math.Max(0, verticalOffset);
        double clampedOffset = Math.Clamp(
            requestedOffset,
            0,
            GetMaximumVerticalOffset(scrollViewer));
        scrollViewer.Offset = scrollViewer.Offset.WithY(clampedOffset);
    }

    private static ScrollViewer? GetScrollViewer(ListBox listBox)
    {
        return listBox
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();
    }

    private static double GetMaximumVerticalOffset(ScrollViewer scrollViewer)
    {
        return Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
    }

    private void CancelSelectionToggle()
    {
        _selectionToggleCandidate = null;
        _selectionToggleOwner = null;
        _selectionToggleCoordinateSpace = null;
    }

    internal static bool IsInteractiveChild(Visual? source)
    {
        for (Visual? current = source; current != null; current = current.GetVisualParent())
        {
            if (current is Button or ComboBox or MenuItem)
            {
                return true;
            }
        }

        return false;
    }

    private static ModificationViewModel? ResolveContentTile(
        ListBox expectedOwner,
        Visual? source,
        bool rejectInteractiveChild)
    {
        if (rejectInteractiveChild && IsInteractiveChild(source))
        {
            return null;
        }

        for (Visual? current = source; current != null; current = current.GetVisualParent())
        {
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
