using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Shared.Archives;
using GenLauncherGO.Shared.IO;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Updating;

internal sealed class SingleFilePackageUpdater : ISingleFilePackageUpdater
{
    private readonly IArchiveExtractor _archiveExtractor;
    private readonly IResumableFileDownloader _fileDownloader;
    private readonly ILogger<SingleFilePackageUpdater> _logger;
    public SingleFilePackageUpdater(
        IResumableFileDownloader fileDownloader,
        IArchiveExtractor archiveExtractor,
        ILogger<SingleFilePackageUpdater> logger)
    {
        _fileDownloader = fileDownloader ?? throw new ArgumentNullException(nameof(fileDownloader));
        _archiveExtractor = archiveExtractor ?? throw new ArgumentNullException(nameof(archiveExtractor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Downloads a single remote package file, optionally extracts it, removes the downloaded archive, and stages the
    ///     temporary folder into the installed package location.
    /// </summary>
    public async Task UpdateAsync(
        DownloadFileMetadata metadata,
        PackageUpdatePathSet paths,
        IProgress<PackageUpdateProgress>? progress,
        CancellationToken cancellationToken,
        PackageDownloadPauseController? pauseController = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(paths);

        await PackageDownloadPauseController.WaitWhilePausedAsync(pauseController, cancellationToken)
            .ConfigureAwait(false);
        string temporaryFolderPath = paths.TemporaryPath.FullPath;
        PackageStagingFolderCleaner.ClearDirectory(paths.TemporaryPath, _logger);
        _logger.LogDebug(
            "Starting single-file package update from {Host}.",
            metadata.DownloadUri.Host);

        string destinationFilePath = ResolveMetadataFilePath(
            temporaryFolderPath,
            metadata.FileName);
        bool extractionRequired = LauncherContentFileTypes.IsArchive(destinationFilePath);
        var progressTracker = new PackageProgressTracker(metadata.TotalBytes);

        IProgress<DownloadProgress> downloadProgress = new InlineProgress<DownloadProgress>(report =>
        {
            PackageUpdateProgress? packageProgress = progressTracker.Update(
                metadata.FileName,
                report.BytesDownloaded,
                report.TotalBytes.HasValue && report.BytesDownloaded >= report.TotalBytes.Value);
            if (packageProgress is not null)
            {
                progress?.Report(packageProgress);
            }
        });

        await _fileDownloader.DownloadFileAsync(
            new DownloadFileRequest(
                metadata.DownloadUri,
                destinationFilePath,
                metadata.TotalBytes,
                true,
                pauseController),
            downloadProgress,
            cancellationToken).ConfigureAwait(false);

        await PackageDownloadPauseController.WaitWhilePausedAsync(pauseController, cancellationToken)
            .ConfigureAwait(false);
        if (extractionRequired)
        {
            await Task.Run(
                () => _archiveExtractor.ExtractToDirectory(
                    destinationFilePath,
                    temporaryFolderPath,
                    true,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destinationFilePath))
            {
                File.Delete(destinationFilePath);
                _logger.LogDebug(
                    "Deleted downloaded archive {FileName} after extraction.",
                    Path.GetFileName(destinationFilePath));
            }
        }

        await PackageDownloadPauseController.WaitWhilePausedAsync(pauseController, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            paths.InstalledPath,
            paths.BackupPath,
            _logger);
        PackageStagingFolderCleaner.DeleteEmptyPackageParents(paths.TemporaryPath, _logger);
        _logger.LogDebug("Completed single-file package update.");
    }

    /// <summary>
    ///     Resolves an HTTP metadata file name as one direct child of the owned staging folder.
    /// </summary>
    private static string ResolveMetadataFilePath(
        string temporaryFolderPath,
        string fileName)
    {
        string safeFileName;
        try
        {
            safeFileName = LexicalPath.NormalizePathSegment(fileName, nameof(fileName));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Remote package metadata did not provide a safe direct file name.",
                exception);
        }

        return ManifestPathResolver.ResolvePath(temporaryFolderPath, safeFileName);
    }
}
