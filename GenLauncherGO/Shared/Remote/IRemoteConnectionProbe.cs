using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenLauncherGO.Shared.Remote;

/// <summary>
///     Checks whether a remote HTTP endpoint can be reached.
/// </summary>
internal interface IRemoteConnectionProbe
{
    /// <summary>
    ///     Returns whether the endpoint responds successfully.
    /// </summary>
    Task<bool> CanConnectAsync(Uri endpointUri, CancellationToken cancellationToken);
}
