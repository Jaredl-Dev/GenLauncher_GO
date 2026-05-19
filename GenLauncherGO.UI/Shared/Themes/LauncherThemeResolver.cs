using Avalonia.Media;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.UI.Shared.Themes;

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
            Pick(theme.GenLauncherBorderColor, fallback.GenLauncherBorderColor),
            Pick(theme.GenLauncherInactiveBorder, fallback.GenLauncherInactiveBorder),
            Pick(theme.GenLauncherInactiveBorder2, fallback.GenLauncherInactiveBorder2),
            Pick(theme.GenLauncherActiveColor, fallback.GenLauncherActiveColor),
            Pick(theme.GenLauncherDarkFillColor, fallback.GenLauncherDarkFillColor),
            Pick(theme.GenLauncherDarkBackGround, fallback.GenLauncherDarkBackGround),
            Pick(theme.GenLauncherLightBackGround, fallback.GenLauncherLightBackGround),
            Pick(theme.GenLauncherDefaultTextColor, fallback.GenLauncherDefaultTextColor),
            Pick(theme.GenLauncherDownloadTextColor, fallback.GenLauncherDownloadTextColor),
            Pick(theme.GenLauncherListBoxSelectionColor1, fallback.ListSelectionStartColor),
            Pick(theme.GenLauncherListBoxSelectionColor2, fallback.ListSelectionMiddleColor),
            Pick(
                theme.GenLauncherButtonSelectionColor,
                fallback.GenLauncherButtonSelectionColor),
            // The remote contract has no separate action or heading colour, so a themed shell draws both in the
            // accent it did supply rather than keeping the previous game's.
            Pick(theme.GenLauncherDefaultTextColor, fallback.GenLauncherActionTextColor),
            Pick(theme.GenLauncherActiveColor, fallback.GenLauncherHeadingTextColor),
            ToHex(fallback.GenLauncherErrorColor.Color),
            Pick(theme.GenLauncherInactiveBorder, fallback.GenLauncherDisabledTextColor),
            Pick(theme.GenLauncherDarkBackGround, fallback.GenLauncherChromeBackground),
            ToHex(fallback.GenLauncherScrimColor.Color),
            backgroundImage ?? fallback.GenLauncherBackgroundImage);
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
