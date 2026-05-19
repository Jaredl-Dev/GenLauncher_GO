using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace GenLauncherGO.UI.Shared.Themes;

/// <summary>
///     Applies launcher theme values to Avalonia resource dictionaries.
/// </summary>
/// <remarks>
///     Production writes application-scoped resources so every open window follows through <c>DynamicResource</c>.
///     Passing a window's own dictionary instead shadows that for one window, which is how a window can preview a
///     palette that is not the active one.
/// </remarks>
internal static class LauncherThemeResourceApplier
{
    /// <summary>
    ///     Applies launcher theme values and, when requested, its background image to a resource dictionary.
    /// </summary>
    public static void Apply(
        IResourceDictionary resources,
        ColorsInfo colors,
        bool includeBackgroundImage = true)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(colors);

        resources["GenLauncherBorderColor"] = colors.GenLauncherBorderColor;
        resources["GenLauncherBorderHighlightColor"] = CreateHighlight(colors.GenLauncherBorderColor.Color);
        resources["GenLauncherActionTextColor"] = colors.GenLauncherActionTextColor;
        resources["GenLauncherHeadingTextColor"] = colors.GenLauncherHeadingTextColor;
        resources["GenLauncherActiveColor"] = colors.GenLauncherActiveColor;
        resources["GenLauncherDarkFillColor"] = colors.GenLauncherDarkFillColor;
        resources["GenLauncherInactiveBorder"] = colors.GenLauncherInactiveBorder;
        resources["GenLauncherInactiveBorder2"] = colors.GenLauncherInactiveBorder2;
        resources["GenLauncherDefaultTextColor"] = colors.GenLauncherDefaultTextColor;
        resources["GenLauncherErrorColor"] = colors.GenLauncherErrorColor;
        resources["GenLauncherDisabledTextColor"] = colors.GenLauncherDisabledTextColor;
        resources["GenLauncherChromeBackground"] = colors.GenLauncherChromeBackground;
        resources["GenLauncherScrimColor"] = colors.GenLauncherScrimColor;
        resources["GenLauncherLightBackGround"] = colors.GenLauncherLightBackGround;
        resources["GenLauncherDarkBackGround"] = colors.GenLauncherDarkBackGround;
        resources["GenLauncherListBoxSelectionColor2"] = colors.ListSelectionMiddleColor;
        resources["GenLauncherHoverBackground"] = CreateHoverBackground(
            colors.GenLauncherButtonSelectionColor,
            colors.GenLauncherLightBackGround.Color);

        // Selection and hover deliberately share one gradient, so a hovered row reads as a preview of selecting it.
        IBrush listSelectionBackground = CreateListBoxSelectionBackground(
            colors.ListSelectionStartColor,
            colors.ListSelectionMiddleColor);
        resources["ListBoxSelectedItemBackground"] = listSelectionBackground;
        resources["ListBoxMouseOverItemBackground"] = listSelectionBackground;

        // Dialog lists are narrow, so the row is filled flat rather than faded out across it. It uses the palette's
        // row-selection colour rather than its button colour: the latter is a full-strength accent meant to sit
        // under a short label, and behind a whole row of text it overpowers the text on top of it.
        resources["DialogListBoxSelectedItemBackground"] =
            new ImmutableSolidColorBrush(colors.ListSelectionMiddleColor);

        if (includeBackgroundImage && colors.GenLauncherBackgroundImage != null)
        {
            resources["GenLauncherBackGroundImage"] = colors.GenLauncherBackgroundImage;
        }
    }

    /// <summary>
    ///     Builds a brighter version of the accent for hover states, so a hovered border reads as the same colour
    ///     lit up rather than as a different one.
    /// </summary>
    /// <remarks>
    ///     Derived rather than stored so it tracks whichever accent the active game supplies.
    /// </remarks>
    private static IImmutableSolidColorBrush CreateHighlight(Color accent)
    {
        const double Lift = 0.65;
        return new ImmutableSolidColorBrush(Color.FromArgb(
            accent.A,
            Lighten(accent.R, Lift),
            Lighten(accent.G, Lift),
            Lighten(accent.B, Lift)));
    }

    private static byte Lighten(byte channel, double amount)
    {
        return (byte)Math.Round(channel + (255 - channel) * amount);
    }

    /// <summary>
    ///     Builds the horizontal wash a control shows while the pointer is over it.
    /// </summary>
    /// <remarks>
    ///     The edges fade into the palette's own panel scrim rather than a fixed black, so the wash still sits on the
    ///     surface behind it when a theme changes that scrim.
    /// </remarks>
    private static IBrush CreateHoverBackground(Color selectionColor, Color edgeColor)
    {
        LinearGradientBrush brush = CreateHorizontalGradientBrush();
        brush.GradientStops.Add(new GradientStop(edgeColor, 0));
        brush.GradientStops.Add(new GradientStop(selectionColor, 0.1));
        brush.GradientStops.Add(new GradientStop(selectionColor, 0.9));
        brush.GradientStops.Add(new GradientStop(edgeColor, 1));
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

    private static LinearGradientBrush CreateHorizontalGradientBrush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative)
        };
    }
}
