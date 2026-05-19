using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace GenLauncherGO.UI.Shared.Controls;

/// <summary>
///     Lets an undecorated window be moved by dragging one of its own controls, usually its title band.
/// </summary>
/// <remarks>
///     Launcher windows draw their own chrome, so without this they cannot be moved at all. Attaching it in markup
///     keeps the behaviour in one place instead of repeating a pointer handler in every window's code-behind.
/// </remarks>
internal static class LauncherWindowDragHandle
{
    /// <summary>
    ///     Identifies the attached property that turns a control into a drag handle for its window.
    /// </summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "IsEnabled",
            typeof(LauncherWindowDragHandle));

    static LauncherWindowDragHandle()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control, bool>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(Control control)
    {
        return control.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(Control control, bool value)
    {
        control.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs<bool> args)
    {
        control.PointerPressed -= OnPointerPressed;
        if (args.NewValue.GetValueOrDefault())
        {
            control.PointerPressed += OnPointerPressed;
        }
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (sender is not Visual visual ||
            TopLevel.GetTopLevel(visual) is not Window window ||
            !eventArgs.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
        {
            return;
        }

        window.BeginMoveDrag(eventArgs);
    }
}
