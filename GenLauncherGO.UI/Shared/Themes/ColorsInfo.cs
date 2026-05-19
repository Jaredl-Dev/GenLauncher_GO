using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace GenLauncherGO.UI.Shared.Themes;

/// <summary>
///     Stores immutable Avalonia brushes and colors for a built-in launcher theme.
/// </summary>
internal sealed class ColorsInfo
{
    internal ColorsInfo(
        string border,
        string inactiveBorder,
        string inactiveBorder2,
        string activeColor,
        string darkFill,
        string darkBackground,
        string lightBackground,
        string text,
        string text2,
        string selectionStartColor,
        string selectionMiddleColor,
        string buttonSelectionColor,
        string actionTextColor,
        string headingTextColor,
        string errorColor,
        string disabledTextColor,
        string chromeBackground,
        string scrimColor,
        IImageBrush? backgroundImage = null)
    {
        GenLauncherActionTextColor = GetColorBrushFromString(actionTextColor);
        GenLauncherHeadingTextColor = GetColorBrushFromString(headingTextColor);
        GenLauncherErrorColor = GetColorBrushFromString(errorColor);
        GenLauncherDisabledTextColor = GetColorBrushFromString(disabledTextColor);
        GenLauncherChromeBackground = GetColorBrushFromString(chromeBackground);
        GenLauncherScrimColor = GetColorBrushFromString(scrimColor);
        GenLauncherBorderColor = GetColorBrushFromString(border);
        GenLauncherInactiveBorder = GetColorBrushFromString(inactiveBorder);
        GenLauncherInactiveBorder2 = GetColorBrushFromString(inactiveBorder2);
        GenLauncherActiveColor = GetColorBrushFromString(activeColor);
        GenLauncherDarkFillColor = GetColorBrushFromString(darkFill);
        GenLauncherDarkBackGround = GetColorBrushFromString(darkBackground);
        GenLauncherLightBackGround = GetColorBrushFromString(lightBackground);
        GenLauncherDefaultTextColor = GetColorBrushFromString(text);
        GenLauncherDownloadTextColor = GetColorBrushFromString(text2);

        ListSelectionStartColor = GetColorFromString(selectionStartColor);
        ListSelectionMiddleColor = GetColorFromString(selectionMiddleColor);
        GenLauncherButtonSelectionColor = GetColorFromString(buttonSelectionColor);
        GenLauncherBackgroundImage = ToImmutableImageBrush(backgroundImage);
    }

    /// <summary>
    ///     Gets the resting colour for button and action text.
    /// </summary>
    /// <remarks>
    ///     Separate from the default text colour because the Generals shell draws action text in its rust accent
    ///     while ordinary labels stay white. Zero Hour supplies white here so it keeps the plain treatment.
    /// </remarks>
    public IImmutableSolidColorBrush GenLauncherActionTextColor { get; }

    public IImmutableSolidColorBrush GenLauncherActiveColor { get; }

    /// <summary>
    ///     Gets the colour for window titles and section headings.
    /// </summary>
    /// <remarks>
    ///     Per game because the Generals shell draws headings in its gold accent while the Zero Hour shell leaves
    ///     them white and carries its accent on the borders instead.
    /// </remarks>
    public IImmutableSolidColorBrush GenLauncherHeadingTextColor { get; }

    public IImageBrush? GenLauncherBackgroundImage { get; }

    public IImmutableSolidColorBrush GenLauncherBorderColor { get; }

    public Color GenLauncherButtonSelectionColor { get; }

    public IImmutableSolidColorBrush GenLauncherDarkBackGround { get; }

    public IImmutableSolidColorBrush GenLauncherDarkFillColor { get; }

    public IImmutableSolidColorBrush GenLauncherDefaultTextColor { get; }

    /// <summary>
    ///     Gets the colour for text drawn on top of a filled progress bar.
    /// </summary>
    public IImmutableSolidColorBrush GenLauncherDownloadTextColor { get; }

    /// <summary>
    ///     Gets the colour for validation messages and unavailable-item markers.
    /// </summary>
    public IImmutableSolidColorBrush GenLauncherErrorColor { get; }

    /// <summary>
    ///     Gets the colour for text and glyphs on disabled controls.
    /// </summary>
    public IImmutableSolidColorBrush GenLauncherDisabledTextColor { get; }

    /// <summary>
    ///     Gets the fill for the title and action bands that frame a window's content.
    /// </summary>
    public IImmutableSolidColorBrush GenLauncherChromeBackground { get; }

    /// <summary>
    ///     Gets the wash drawn over the launcher while a modal overlay is showing.
    /// </summary>
    public IImmutableSolidColorBrush GenLauncherScrimColor { get; }

    public IImmutableSolidColorBrush GenLauncherInactiveBorder { get; }

    public IImmutableSolidColorBrush GenLauncherInactiveBorder2 { get; }

    public IImmutableSolidColorBrush GenLauncherLightBackGround { get; }

    public Color ListSelectionStartColor { get; }

    public Color ListSelectionMiddleColor { get; }

    private static IImmutableSolidColorBrush GetColorBrushFromString(string hex)
    {
        return new ImmutableSolidColorBrush(GetColorFromString(hex));
    }

    private static Color GetColorFromString(string hex)
    {
        return Color.Parse(hex);
    }

    private static IImageBrush? ToImmutableImageBrush(IImageBrush? brush)
    {
        return brush == null
            ? null
            : (IImageBrush)brush.ToImmutable();
    }
}
