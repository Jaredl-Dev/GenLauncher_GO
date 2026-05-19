using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace GenLauncherGO.UI.Shared.Themes;

/// <summary>
/// Applies launcher theme values to Avalonia resource dictionaries.
/// </summary>
internal static class LauncherThemeResourceApplier
{
    private static readonly IImmutableSolidColorBrush _neutralSurfaceColor =
        new ImmutableSolidColorBrush(Color.FromArgb(0xE6, 0x0A, 0x0D, 0x10));

    private static readonly IImmutableSolidColorBrush _neutralBorderColor =
        new ImmutableSolidColorBrush(Color.FromRgb(0x5A, 0x62, 0x6B));

    private static readonly IImmutableSolidColorBrush _neutralPressedColor =
        new ImmutableSolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3D));

    private static readonly IImmutableSolidColorBrush _secondaryTextColor =
        new ImmutableSolidColorBrush(Color.FromRgb(0xD2, 0xD8, 0xE0));

    /// <summary>
    /// Applies launcher theme values and, when requested, its background image to a framework element.
    /// </summary>
    public static void Apply(
        StyledElement element,
        ColorsInfo colors,
        bool includeBackgroundImage = true)
    {
        ArgumentNullException.ThrowIfNull(element);
        Apply(element.Resources, colors, includeBackgroundImage);
    }

    /// <summary>
    /// Applies launcher theme values and, when requested, its background image to a resource dictionary.
    /// </summary>
    public static void Apply(
        IResourceDictionary resources,
        ColorsInfo colors,
        bool includeBackgroundImage = true)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(colors);

        resources["GenLauncherBorderColor"] = colors.GenLauncherBorderColor;
        resources["GenLauncherNeutralSurfaceColor"] = _neutralSurfaceColor;
        resources["GenLauncherNeutralBorderColor"] = _neutralBorderColor;
        resources["GenLauncherNeutralPressedColor"] = _neutralPressedColor;
        resources["GenLauncherSecondaryTextColor"] = _secondaryTextColor;
        resources["GenLauncherAccentFillColor"] = colors.GenLauncherAccentFillColor;
        resources["GenLauncherSubtleAccentFillColor"] = colors.GenLauncherSubtleAccentFillColor;
        resources["GenLauncherAccentBorderColor"] = colors.GenLauncherAccentBorderColor;
        resources["GenLauncherSubtleAccentBorderColor"] = colors.GenLauncherSubtleAccentBorderColor;
        resources["GenLauncherAccentTextColor"] = colors.GenLauncherAccentTextColor;
        Color buttonSelectionColor = colors.GenLauncherButtonSelectionColor;
        resources["ButtonPointerOverBackground"] = CreateButtonInteractionBackground(
            colors.GenLauncherButtonPointerOverColor);
        resources["GenLauncherActiveColor"] = colors.GenLauncherActiveColor;
        resources["GenLauncherDarkFillColor"] = colors.GenLauncherDarkFillColor;
        resources["GenLauncherInactiveBorder"] = colors.GenLauncherInactiveBorder;
        resources["GenLauncherInactiveBorder2"] = colors.GenLauncherInactiveBorder2;
        resources["GenLauncherDefaultTextColor"] = colors.GenLauncherDefaultTextColor;
        resources["GenLauncherDownloadTextColor"] = colors.GenLauncherDownloadTextColor;
        resources["GenLauncherLightBackGround"] = colors.GenLauncherLightBackGround;
        resources["GenLauncherDarkBackGround"] = colors.GenLauncherDarkBackGround;
        resources["GenLauncherListBoxSelectionColor1"] = colors.ListSelectionStartColor;
        resources["GenLauncherListBoxSelectionColor2"] = colors.ListSelectionMiddleColor;
        resources["GenLauncherButtonSelectionColor"] = colors.GenLauncherButtonSelectionColor;
        resources["ButtonPressedBackground"] = CreateButtonInteractionBackground(buttonSelectionColor);
        resources["ListBoxSelectedItemBackground"] = CreateListBoxSelectionBackground(
            colors.ListSelectionStartColor,
            colors.ListSelectionMiddleColor);
        resources["DialogListBoxSelectedItemBackground"] = CreateSolidSelectionBackground(
            colors.GenLauncherButtonSelectionColor);
        resources["ListBoxMouseOverItemBackground"] = CreateListBoxSelectionBackground(
            colors.ListSelectionStartColor,
            colors.ListSelectionMiddleColor);

        if (includeBackgroundImage && colors.GenLauncherBackgroundImage != null)
        {
            resources["GenLauncherBackGroundImage"] = colors.GenLauncherBackgroundImage;
        }
    }

    private static IBrush CreateButtonInteractionBackground(Color selectionColor)
    {
        LinearGradientBrush brush = CreateHorizontalGradientBrush();
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xB3, 0x00, 0x00, 0x00), 0));
        brush.GradientStops.Add(new GradientStop(selectionColor, 0.1));
        brush.GradientStops.Add(new GradientStop(selectionColor, 0.9));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xB3, 0x00, 0x00, 0x00), 1));
        return brush.ToImmutable();
    }

    private static IBrush CreateListBoxSelectionBackground(
        Color selectionStartColor,
        Color selectionMiddleColor)
    {
        LinearGradientBrush brush = CreateHorizontalGradientBrush();
        brush.GradientStops.Add(new GradientStop(selectionStartColor, 0.0));
        brush.GradientStops.Add(new GradientStop(selectionMiddleColor, 0.7));
        brush.GradientStops.Add(new GradientStop(
            Color.FromArgb(
                0,
                selectionMiddleColor.R,
                selectionMiddleColor.G,
                selectionMiddleColor.B),
            1.0));
        return brush.ToImmutable();
    }

    private static IBrush CreateSolidSelectionBackground(Color selectionColor)
    {
        LinearGradientBrush brush = CreateHorizontalGradientBrush();
        brush.GradientStops.Add(new GradientStop(selectionColor, 0.0));
        brush.GradientStops.Add(new GradientStop(selectionColor, 1.0));
        return brush.ToImmutable();
    }

    private static LinearGradientBrush CreateHorizontalGradientBrush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        };
    }
}
