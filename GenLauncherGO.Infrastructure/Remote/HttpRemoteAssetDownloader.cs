using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Remote.Contracts;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Remote;

internal sealed class HttpRemoteAssetDownloader : IRemoteAssetDownloader
{
    private readonly IResumableFileDownloader _fileDownloader;
    private readonly ILogger<HttpRemoteAssetDownloader> _logger;

    public HttpRemoteAssetDownloader(
        IResumableFileDownloader fileDownloader,
        ILogger<HttpRemoteAssetDownloader> logger)
    {
        _fileDownloader = fileDownloader ?? throw new ArgumentNullException(nameof(fileDownloader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Downloads a remote asset to a temporary file and atomically moves it into place when the final file is missing.
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

        Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath) ?? ".");
        string temporaryFilePath = destinationFilePath + ".download";
        if (File.Exists(temporaryFilePath))
        {
            File.Delete(temporaryFilePath);
            _logger.LogInformation(
                "Deleted stale remote asset download file {FileName}.",
                Path.GetFileName(temporaryFilePath));
        }

        await _fileDownloader.DownloadFileAsync(
            new DownloadFileRequest(sourceUri, temporaryFilePath, Resume: false),
            null,
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(destinationFilePath))
        {
            File.Delete(temporaryFilePath);
            return;
        }

        File.Move(temporaryFilePath, destinationFilePath);
        _logger.LogInformation(
            "Downloaded remote asset {FileName} from {Host}.",
            Path.GetFileName(destinationFilePath),
            sourceUri.Host);
    }
}
