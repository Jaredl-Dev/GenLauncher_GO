namespace GenLauncherGO.UI.Features.Launcher.Models;

/// <summary>
///     Identifies which external link on a modification tile a user activated.
/// </summary>
internal enum LauncherTileLinkKind
{
    /// <summary>
    ///     The release notes or news page for the tile's latest version.
    /// </summary>
    ChangeLog,

    /// <summary>
    ///     The multiplayer or network setup information page.
    /// </summary>
    NetworkInfo,

    /// <summary>
    ///     The author's donation or support page.
    /// </summary>
    Support,

    /// <summary>
    ///     The Mod DB listing.
    /// </summary>
    ModDb,

    /// <summary>
    ///     The community Discord invitation.
    /// </summary>
    Discord
}
