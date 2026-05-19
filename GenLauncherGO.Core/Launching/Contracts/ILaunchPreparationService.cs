using System.Threading;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Core.Launching.Contracts;

/// <summary>
///     Prepares, cleans, and recovers launch-time game-directory state.
/// </summary>
public interface ILaunchPreparationService
{
    /// <summary>
    ///     Prepares the game directory for launching the selected content.
    /// </summary>
    /// <returns><see langword="true" /> when preparation completed successfully.</returns>
    bool Prepare(
        LaunchPreparationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Cleans launch-time game-directory state after a launched process exits.
    /// </summary>
    /// <returns><see langword="true" /> when cleanup completed successfully.</returns>
    bool Cleanup(
        LauncherPaths paths,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Recovers interrupted launch-time game-directory state during launcher startup.
    /// </summary>
    /// <returns><see langword="true" /> when recovery completed successfully.</returns>
    bool Recover(
        LauncherPaths paths,
        CancellationToken cancellationToken);
}
