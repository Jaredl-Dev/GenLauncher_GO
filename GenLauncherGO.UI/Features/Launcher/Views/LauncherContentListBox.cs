using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using GenLauncherGO.UI.Features.Launcher.Support;
using GenLauncherGO.UI.Features.Mods;

namespace GenLauncherGO.UI.Features.Launcher.Views;

/// <summary>
///     Preserves content-tile selection while a version action inside its popup is being invoked.
/// </summary>
internal sealed class LauncherContentListBox : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);

    protected override bool ShouldTriggerSelection(Visual selectable, PointerEventArgs eventArgs)
    {
        bool isVersionPopupContent =
            eventArgs.Source is Control { DataContext: ModificationVersionSelection } ||
            eventArgs.Source is Visual source && source.GetVisualAncestors()
                .OfType<Control>()
                .Any(control => control.DataContext is ModificationVersionSelection);
        bool isInteractiveChild = LauncherDragDropController.IsInteractiveChild(
            eventArgs.Source as Visual);

        return !isInteractiveChild &&
               !isVersionPopupContent &&
               base.ShouldTriggerSelection(selectable, eventArgs);
    }
}
