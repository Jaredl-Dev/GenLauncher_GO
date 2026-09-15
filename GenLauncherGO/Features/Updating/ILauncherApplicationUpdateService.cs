using System.Threading;
using System.Threading.Tasks;

namespace GenLauncherGO.Features.Updating;

/// <summary>
///     Checks, downloads, and hands off portable launcher application updates at the external updater boundary.
/// </summary>
internal interface ILauncherApplicationUpdateService
{
    /// <summary>
    ///     Surfaces an already-downloaded update, or checks for and downloads one when the automatic-check interval
    ///     permits it.
    /// </summary>
    /// <returns>The downloaded version string, or <see langword="null" /> when no prompt is needed.</returns>
    Task<string?> CheckAndDownloadUpdateAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Starts the updater after application cleanup and asks it to wait for this process to exit before applying.
    /// </summary>
    /// <returns><see langword="true" /> when the updater handoff started successfully.</returns>
    bool TryHandoffToUpdateAndRestart();
}
