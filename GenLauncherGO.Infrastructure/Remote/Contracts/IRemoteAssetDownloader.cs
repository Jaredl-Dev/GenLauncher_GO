using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenLauncherGO.Infrastructure.Remote.Contracts;

internal interface IRemoteAssetDownloader
{
    /// <summary>
    ///     Downloads an asset only when the destination file is not already present.
    /// </summary>
    Task DownloadIfMissingAsync(
        Uri sourceUri,
        string destinationFilePath,
        CancellationToken cancellationToken);
}
