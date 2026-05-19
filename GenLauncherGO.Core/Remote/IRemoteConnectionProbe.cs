using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenLauncherGO.Core.Remote;

/// <summary>
///     Checks whether a remote HTTP endpoint can be reached.
/// </summary>
public interface IRemoteConnectionProbe
{
    /// <summary>
    ///     Returns whether the endpoint responds successfully.
    /// </summary>
    Task<bool> CanConnectAsync(Uri endpointUri, CancellationToken cancellationToken);
}
