using Avalonia.Media;
using GenLauncherGO.Features.Mods;
using GenLauncherGO.Features.Startup;

namespace GenLauncherGO.Shared.Themes;

/// <summary>
///     Builds the launcher palette for a selected modification, falling back to the active game's palette slot by slot.
/// </summary>
/// <remarks>
///     A published modification may supply any subset of the remote palette, and the launcher carries slots the remote
///     contract never had. Resolving per slot means a modification that declares only an accent still gets a coherent
///     shell, and a slot the backend published as nonsense degrades to the built-in colour instead of failing startup.
/// </remarks>
internal static class LauncherThemeResolver
{
    /// <summary>
    ///     Resolves the palette to wear for <paramref name="theme" /> over the built-in palette of
    ///     <paramref name="managedGame" />.
    /// </summary>
    /// <param name="theme">The published palette, or <see langword="null" /> to use the built-in one unchanged.</param>
    /// <param name="managedGame">The active game whose palette supplies every unspecified slot.</param>
    /// <param name="backgroundImage">
    ///     The modification's cached background artwork, or <see langword="null" /> to keep the game's artwork.
    /// </param>
    public static ColorsInfo Resolve(
        LauncherContentTheme? theme,
        SupportedGame managedGame,
        IImageBrush? backgroundImage = null)
    {
        ColorsInfo fallback = LauncherThemePresets.Create(managedGame);
        if (theme is null)
        {
            return fallback;
        }

        return new ColorsInfo(
            borderColor: Pick(theme.GenLauncherBorderColor, fallback.GenLauncherBorderColor),
            inactiveBorderColor: Pick(theme.GenLauncherInactiveBorder, fallback.GenLauncherInactiveBorder),
            inactiveBorder2: Pick(theme.GenLauncherInactiveBorder2, fallback.GenLauncherInactiveBorder2),
            activeColor: Pick(theme.GenLauncherActiveColor, fallback.GenLauncherActiveColor),
            darkFillColor: Pick(theme.GenLauncherDarkFillColor, fallback.GenLauncherDarkFillColor),
            darkBackgroundColor: Pick(theme.GenLauncherDarkBackGround, fallback.GenLauncherDarkBackGround),
            lightBackgroundColor: Pick(theme.GenLauncherLightBackGround, fallback.GenLauncherLightBackGround),
            defaultTextColor: Pick(theme.GenLauncherDefaultTextColor, fallback.GenLauncherDefaultTextColor),
            downloadTextColor: Pick(theme.GenLauncherDownloadTextColor, fallback.GenLauncherDownloadTextColor),
            selectionStartColor: Pick(theme.GenLauncherListBoxSelectionColor1, fallback.ListSelectionStartColor),
            selectionMiddleColor: Pick(theme.GenLauncherListBoxSelectionColor2, fallback.ListSelectionMiddleColor),
            buttonSelectionColor: Pick(
                theme.GenLauncherButtonSelectionColor,
                fallback.GenLauncherButtonSelectionColor),
            // The remote contract has no separate action or heading colour, so a themed shell draws both in the
            // accent it did supply rather than keeping the previous game's.
            actionTextColor: Pick(theme.GenLauncherDefaultTextColor, fallback.GenLauncherActionTextColor),
            headingTextColor: Pick(theme.GenLauncherActiveColor, fallback.GenLauncherHeadingTextColor),
            errorColor: ToHex(fallback.GenLauncherErrorColor.Color),
            disabledTextColor: Pick(theme.GenLauncherInactiveBorder, fallback.GenLauncherDisabledTextColor),
            chromeBackgroundColor: Pick(theme.GenLauncherDarkBackGround, fallback.GenLauncherChromeBackground),
            scrimColor: ToHex(fallback.GenLauncherScrimColor.Color),
            backgroundImage: backgroundImage ?? fallback.GenLauncherBackgroundImage);
    }

    /// <summary>
    ///     Reports whether a published colour string can actually be rendered.
    /// </summary>
    public static bool IsRenderableColor(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && Color.TryParse(value, out _);
    }

    private static string Pick(string published, ISolidColorBrush fallback)
    {
        return Pick(published, fallback.Color);
    }

    private static string Pick(string published, Color fallback)
    {
        return IsRenderableColor(published) ? published : ToHex(fallback);
    }

    private static string ToHex(Color color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
