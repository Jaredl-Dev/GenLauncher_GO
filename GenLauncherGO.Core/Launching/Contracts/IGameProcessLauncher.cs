using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Launching.Models;

namespace GenLauncherGO.Core.Launching.Contracts;

/// <summary>
/// Launches supported game and tool processes for a prepared game directory.
/// </summary>
public interface IGameProcessLauncher
{
    /// <summary>
    /// Starts the requested game or tool process and returns an operation that tracks its process family.
    /// </summary>
    Task<IGameProcessLaunchOperation> StartAsync(
        GameLaunchRequest request,
        CancellationToken cancellationToken);
}
