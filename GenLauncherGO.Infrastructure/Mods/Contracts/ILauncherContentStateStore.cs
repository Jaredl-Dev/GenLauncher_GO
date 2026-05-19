using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Mods.Models;

namespace GenLauncherGO.Infrastructure.Mods.Contracts;

/// <summary>
///     Loads and saves the compact launcher content state.
/// </summary>
internal interface ILauncherContentStateStore
{
    /// <summary>
    ///     Loads persisted launcher content state, returning an empty state when none can be loaded.
    /// </summary>
    LauncherContentState Load(LauncherPaths paths);

    /// <summary>
    ///     Saves launcher content state.
    /// </summary>
    void Save(LauncherPaths paths, LauncherContentState state);
}
