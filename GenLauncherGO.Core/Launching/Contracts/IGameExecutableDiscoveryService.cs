using System.Collections.Generic;
using GenLauncherGO.Core.Launching.Models;

namespace GenLauncherGO.Core.Launching.Contracts;

/// <summary>
///     Discovers game and World Builder executables available to the current launcher session.
/// </summary>
public interface IGameExecutableDiscoveryService
{
    /// <summary>
    ///     Gets the built-in game client executables for the active game installation.
    /// </summary>
    IReadOnlyList<BuiltInExecutable> GetGameClients();

    /// <summary>
    ///     Gets the built-in World Builder executables for the active game installation.
    /// </summary>
    IReadOnlyList<BuiltInExecutable> GetWorldBuilders();

    /// <summary>
    ///     Determines whether a root-level executable file name is currently available and safe to launch.
    /// </summary>
    bool IsExecutableAvailable(string? executableName);
}
