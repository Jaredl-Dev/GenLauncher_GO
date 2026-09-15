using System.Threading;
using System.Threading.Tasks;

namespace GenLauncherGO.Features.Launching;

/// <summary>
///     Launches supported game and tool processes for a prepared game directory.
/// </summary>
internal interface IGameProcessLauncher
{
    /// <summary>
    ///     Starts the requested game or tool process and returns an operation that tracks its process family.
    /// </summary>
    Task<IGameProcessLaunchOperation> StartAsync(
        GameLaunchRequest request,
        CancellationToken cancellationToken);
}
