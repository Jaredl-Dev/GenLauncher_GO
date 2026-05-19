using System.Threading;
using System.Threading.Tasks;

namespace GenLauncherGO.Infrastructure.Launching.Support;

/// <summary>
///     Starts a process and waits until the launched process family has exited.
/// </summary>
internal interface IProcessFamilyLauncher
{
    /// <summary>
    ///     Starts the executable and returns an operation that tracks the launched process family.
    /// </summary>
    Task<IProcessFamilyLaunchOperation> StartAsync(
        string executableName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken);
}
