using System.Collections.Generic;

namespace GenLauncherGO.Features.Launching;

/// <summary>
///     Discovers game and World Builder executables available to the current launcher session.
/// </summary>
internal interface IGameExecutableDiscoveryService
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
    ///     Determines whether a root-level file name or absolute executable path is available and safe to launch.
    /// </summary>
    bool IsExecutableAvailable(string? executablePath);
}
