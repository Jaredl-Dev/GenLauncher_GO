using System;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace GenLauncherGO.UI.Shared.Themes;

/// <summary>
/// Stores immutable Avalonia brushes and colors for a built-in launcher theme.
/// </summary>
internal sealed class ColorsInfo
{
    public IImmutableSolidColorBrush GenLauncherAccentFillColor { get; }

    public IImmutableSolidColorBrush GenLauncherAccentBorderColor { get; }

    public IImmutableSolidColorBrush GenLauncherAccentTextColor { get; }

    public IImmutableSolidColorBrush GenLauncherActiveColor { get; }

    public IImageBrush? GenLauncherBackgroundImage { get; }

    public IImmutableSolidColorBrush GenLauncherBorderColor { get; }

    public Color GenLauncherButtonSelectionColor { get; }

    public Color GenLauncherButtonPointerOverColor { get; }

    public IImmutableSolidColorBrush GenLauncherDarkBackGround { get; }

    public IImmutableSolidColorBrush GenLauncherDarkFillColor { get; }

    public IImmutableSolidColorBrush GenLauncherDefaultTextColor { get; }

    public IImmutableSolidColorBrush GenLauncherDownloadTextColor { get; }

    public IImmutableSolidColorBrush GenLauncherInactiveBorder { get; }

    public IImmutableSolidColorBrush GenLauncherInactiveBorder2 { get; }

    public IImmutableSolidColorBrush GenLauncherLightBackGround { get; }

    public IImmutableSolidColorBrush GenLauncherSubtleAccentBorderColor { get; }

    public IImmutableSolidColorBrush GenLauncherSubtleAccentFillColor { get; }

    public Color ListSelectionStartColor { get; }

    public Color ListSelectionMiddleColor { get; }

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
        string buttonPointerOverColor,
        string accentFillColor,
        string subtleAccentFillColor,
        string accentBorderColor,
        string subtleAccentBorderColor,
        string accentTextColor,
        IImageBrush? backgroundImage = null)
    {
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
        GenLauncherButtonPointerOverColor = GetColorFromString(buttonPointerOverColor);
        GenLauncherAccentFillColor = GetColorBrushFromString(accentFillColor);
        GenLauncherSubtleAccentFillColor = GetColorBrushFromString(subtleAccentFillColor);
        GenLauncherAccentBorderColor = GetColorBrushFromString(accentBorderColor);
        GenLauncherSubtleAccentBorderColor = GetColorBrushFromString(subtleAccentBorderColor);
        GenLauncherAccentTextColor = GetColorBrushFromString(accentTextColor);
        GenLauncherBackgroundImage = ToImmutableImageBrush(backgroundImage);
    }

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
            : (IImageBrush)((IBrush)brush).ToImmutable();
    }
}
