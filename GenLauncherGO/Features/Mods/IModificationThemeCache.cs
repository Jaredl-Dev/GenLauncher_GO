
namespace GenLauncherGO.Features.Mods;

/// <summary>
///     Remembers the palette a modification published, so a themed launcher survives an offline restart.
/// </summary>
/// <remarks>
///     This is a cache of remote manifest data, never an authority: whenever the catalog reaches the backend the
///     published palette wins, and a missing or unreadable entry simply means the launcher wears the active game's
///     palette instead. Entries live beside the modification's cached artwork and are removed with it.
/// </remarks>
internal interface IModificationThemeCache
{
    /// <summary>
    ///     Stores the palette published for one content version, replacing any previously cached entry.
    /// </summary>
    void Save(LauncherContentKey contentKey, LauncherContentTheme theme);

    /// <summary>
    ///     Loads the palette cached for one content version, or <see langword="null" /> when none is available.
    /// </summary>
    LauncherContentTheme? Load(LauncherContentKey contentKey);
}
