namespace GenLauncherGO.Infrastructure.Mods.Models;

/// <summary>
///     Represents the per-modification palette using the exact property names accepted from the legacy remote backend.
/// </summary>
/// <remarks>
///     The backend publishes this as a nested <c>ColorsInformation</c> mapping with PascalCase keys and no naming
///     convention applied, so these names are part of the external contract and must not be renamed.
/// </remarks>
internal sealed class LegacyContentThemeManifest
{
    public string GenLauncherBorderColor { get; set; } = string.Empty;

    public string GenLauncherInactiveBorder { get; set; } = string.Empty;

    public string GenLauncherInactiveBorder2 { get; set; } = string.Empty;

    public string GenLauncherActiveColor { get; set; } = string.Empty;

    public string GenLauncherDarkFillColor { get; set; } = string.Empty;

    public string GenLauncherDarkBackGround { get; set; } = string.Empty;

    public string GenLauncherLightBackGround { get; set; } = string.Empty;

    public string GenLauncherDefaultTextColor { get; set; } = string.Empty;

    public string GenLauncherDownloadTextColor { get; set; } = string.Empty;

    public string GenLauncherListBoxSelectionColor1 { get; set; } = string.Empty;

    public string GenLauncherListBoxSelectionColor2 { get; set; } = string.Empty;

    public string GenLauncherButtonSelectionColor { get; set; } = string.Empty;

    public string GenLauncherBackgroundImageLink { get; set; } = string.Empty;
}
