namespace GenLauncherGO.Core.Mods.Models;

/// <summary>
///     Identifies a launcher content category.
/// </summary>
public enum ModificationType
{
    /// <summary>
    ///     A game modification.
    /// </summary>
    Mod = 0,

    /// <summary>
    ///     An add-on for a game modification or patch.
    /// </summary>
    Addon = 1,

    /// <summary>
    ///     A patch for a game modification or the original game.
    /// </summary>
    Patch = 2,

    /// <summary>
    ///     Advertising content displayed in the launcher.
    /// </summary>
    Advertising = 3
}
