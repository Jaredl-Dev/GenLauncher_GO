namespace GenLauncherGO.Core.Mods.Models;

/// <summary>
///     Describes the palette a published modification asks the launcher to wear while it is selected.
/// </summary>
/// <remarks>
///     Property names match the remote manifest's <c>ColorsInformation</c> keys, which are also the launcher's own
///     resource key names. Values stay as the raw strings the backend published: they are colour literals in a
///     presentation format this layer deliberately knows nothing about, so parsing and per-slot fallback happen in the
///     UI where the palette is actually built. Any slot may be absent, and a modification may supply none at all.
/// </remarks>
public sealed class LauncherContentTheme
{
    public string GenLauncherBorderColor { get; init; } = string.Empty;

    public string GenLauncherInactiveBorder { get; init; } = string.Empty;

    public string GenLauncherInactiveBorder2 { get; init; } = string.Empty;

    public string GenLauncherActiveColor { get; init; } = string.Empty;

    public string GenLauncherDarkFillColor { get; init; } = string.Empty;

    public string GenLauncherDarkBackGround { get; init; } = string.Empty;

    public string GenLauncherLightBackGround { get; init; } = string.Empty;

    public string GenLauncherDefaultTextColor { get; init; } = string.Empty;

    public string GenLauncherDownloadTextColor { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the colour the list-selection gradient starts from.
    /// </summary>
    /// <remarks>
    ///     The remote name is <c>...Color1</c>. Upstream swapped this with <c>...Color2</c> on the manifest path only,
    ///     so a published mod's documented start colour actually rendered at the far stop. This launcher maps the
    ///     names as documented instead, which is the behaviour mod authors were writing against.
    /// </remarks>
    public string GenLauncherListBoxSelectionColor1 { get; init; } = string.Empty;

    public string GenLauncherListBoxSelectionColor2 { get; init; } = string.Empty;

    public string GenLauncherButtonSelectionColor { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the absolute URL of the artwork drawn behind the launcher shell.
    /// </summary>
    public string GenLauncherBackgroundImageLink { get; init; } = string.Empty;

    /// <summary>
    ///     Gets a value indicating whether this theme publishes at least one usable palette or artwork value.
    /// </summary>
    public bool HasValues =>
        !string.IsNullOrWhiteSpace(GenLauncherBorderColor) ||
        !string.IsNullOrWhiteSpace(GenLauncherInactiveBorder) ||
        !string.IsNullOrWhiteSpace(GenLauncherInactiveBorder2) ||
        !string.IsNullOrWhiteSpace(GenLauncherActiveColor) ||
        !string.IsNullOrWhiteSpace(GenLauncherDarkFillColor) ||
        !string.IsNullOrWhiteSpace(GenLauncherDarkBackGround) ||
        !string.IsNullOrWhiteSpace(GenLauncherLightBackGround) ||
        !string.IsNullOrWhiteSpace(GenLauncherDefaultTextColor) ||
        !string.IsNullOrWhiteSpace(GenLauncherDownloadTextColor) ||
        !string.IsNullOrWhiteSpace(GenLauncherListBoxSelectionColor1) ||
        !string.IsNullOrWhiteSpace(GenLauncherListBoxSelectionColor2) ||
        !string.IsNullOrWhiteSpace(GenLauncherButtonSelectionColor) ||
        !string.IsNullOrWhiteSpace(GenLauncherBackgroundImageLink);

    /// <summary>
    ///     Builds the cache file name a version's shell artwork is stored under.
    /// </summary>
    /// <remarks>
    ///     This is the single authority for the name. The download cache writes it, the presentation layer reads it,
    ///     content removal deletes it, and integrity scanning skips it for inactive versions — they only agree because
    ///     they all call here. The suffix keeps it clear of the tile image, which is cached under the bare version.
    /// </remarks>
    public static string ResolveBackgroundImageBaseName(string version)
    {
        return (version ?? string.Empty) + "-background";
    }

    /// <summary>
    ///     Builds the cache file name a version's palette is stored under, so it survives an offline restart.
    /// </summary>
    /// <remarks>
    ///     The palette is cached beside the artwork it belongs to, which gives both the same lifetime: removing a
    ///     modification removes its cache folder and everything in it.
    /// </remarks>
    public static string ResolveCacheBaseName(string version)
    {
        return (version ?? string.Empty) + "-theme";
    }
}
