using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Features.Updating;
using GenLauncherGO.Shared.Persistence;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Shared.Remote;

internal sealed class HttpRemoteAssetDownloader : IRemoteAssetDownloader
{
    private readonly IResumableFileDownloader _fileDownloader;
    private readonly IAtomicFileWriter _atomicFileWriter;
    private readonly ILogger<HttpRemoteAssetDownloader> _logger;

    public HttpRemoteAssetDownloader(
        IResumableFileDownloader fileDownloader,
        IAtomicFileWriter atomicFileWriter,
        ILogger<HttpRemoteAssetDownloader> logger)
    {
        _fileDownloader = fileDownloader ?? throw new ArgumentNullException(nameof(fileDownloader));
        _atomicFileWriter = atomicFileWriter ?? throw new ArgumentNullException(nameof(atomicFileWriter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Downloads a remote asset to a temporary file and atomically moves it into place when the final file is missing.
    /// </summary>
    public async Task DownloadIfMissingAsync(
        Uri sourceUri,
        string destinationFilePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);

        if (File.Exists(destinationFilePath))
        {
            return;
        }

        string legacyTemporaryFilePath = destinationFilePath + ".download";
        if (File.Exists(legacyTemporaryFilePath))
        {
            File.Delete(legacyTemporaryFilePath);
            _logger.LogDebug(
                "Deleted stale remote asset download file {FileName}.",
                Path.GetFileName(legacyTemporaryFilePath));
        }

        bool committed = await _atomicFileWriter.WriteFileIfMissingAsync(
            destinationFilePath,
            (temporaryFilePath, token) => _fileDownloader.DownloadFileAsync(
                new DownloadFileRequest(sourceUri, temporaryFilePath, Resume: false),
                null,
                token),
            cancellationToken).ConfigureAwait(false);
        if (committed)
        {
            _logger.LogDebug(
                "Downloaded remote asset {FileName} from {Host}.",
                    Path.GetFileName(destinationFilePath),
                    sourceUri.Host);
        }
    }
}
