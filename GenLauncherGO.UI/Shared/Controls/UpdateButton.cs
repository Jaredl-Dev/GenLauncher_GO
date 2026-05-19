using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Input;

namespace GenLauncherGO.UI.Shared.Controls;

/// <summary>
///     Represents a launcher action button with a temporary blinking overlay.
/// </summary>
[PseudoClasses(":blinking")]
public sealed class UpdateButton : Button
{
    private const string BlinkingPseudoClass = ":blinking";

    /// <summary>
    ///     Identifies the <see cref="IsBlinking" /> styled property.
    /// </summary>
    public static readonly StyledProperty<bool> IsBlinkingProperty =
        AvaloniaProperty.Register<UpdateButton, bool>(nameof(IsBlinking));

    /// <summary>
    ///     Gets or sets a value indicating whether the button should run the blink animation.
    /// </summary>
    public bool IsBlinking
    {
        get => GetValue(IsBlinkingProperty);
        set => SetValue(IsBlinkingProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPointerEntered(PointerEventArgs eventArgs)
    {
        base.OnPointerEntered(eventArgs);
        IsBlinking = false;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsBlinkingProperty)
        {
            PseudoClasses.Set(BlinkingPseudoClass, IsBlinking);
        }
    }
}
