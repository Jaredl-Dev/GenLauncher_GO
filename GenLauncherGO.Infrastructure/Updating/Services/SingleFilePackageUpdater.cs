using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Archives;
using GenLauncherGO.Infrastructure.Archives.Contracts;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Support;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Updating.Services;

internal sealed class SingleFilePackageUpdater : ISingleFilePackageUpdater
{
    private readonly IArchiveExtractor _archiveExtractor;
    private readonly IResumableFileDownloader _fileDownloader;
    private readonly ILogger<SingleFilePackageUpdater> _logger;
    private readonly IDownloadFileMetadataReader _metadataReader;

    public SingleFilePackageUpdater(
        IResumableFileDownloader fileDownloader,
        IDownloadFileMetadataReader metadataReader,
        IArchiveExtractor archiveExtractor,
        ILogger<SingleFilePackageUpdater> logger)
    {
        _fileDownloader = fileDownloader ?? throw new ArgumentNullException(nameof(fileDownloader));
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _archiveExtractor = archiveExtractor ?? throw new ArgumentNullException(nameof(archiveExtractor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Downloads a single remote package file, optionally extracts it, removes the downloaded archive, and stages the
    /// temporary folder into the installed package location.
    /// </summary>
    public async Task UpdateAsync(
        Uri sourceUri,
        PackageUpdatePathSet paths,
        IProgress<PackageUpdateProgress>? progress,
        CancellationToken cancellationToken,
        PackageDownloadPauseController? pauseController = null)
    {
        ArgumentNullException.ThrowIfNull(sourceUri);
        ArgumentNullException.ThrowIfNull(paths);

        await WaitWhilePausedAsync(pauseController, cancellationToken).ConfigureAwait(false);
        string temporaryFolderPath = paths.TemporaryPath.FullPath;
        PackageStagingFolderCleaner.ClearDirectory(paths.TemporaryPath, _logger);
        _logger.LogInformation(
            "Starting single-file package update from {Host}.",
            sourceUri.Host);

        DownloadFileMetadata metadata = await _metadataReader.ReadMetadataAsync(
            sourceUri,
            cancellationToken).ConfigureAwait(false);
        await WaitWhilePausedAsync(pauseController, cancellationToken).ConfigureAwait(false);

        string destinationFilePath = ResolveMetadataFilePath(
            temporaryFolderPath,
            metadata.FileName);
        bool extractionRequired = ArchiveFileSupport.IsSupported(destinationFilePath);
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
                Resume: true,
                PauseController: pauseController),
            downloadProgress,
            cancellationToken).ConfigureAwait(false);

        await WaitWhilePausedAsync(pauseController, cancellationToken).ConfigureAwait(false);
        if (extractionRequired)
        {
            await Task.Run(
                () => _archiveExtractor.ExtractToDirectory(
                    destinationFilePath,
                    temporaryFolderPath,
                    convertBigFilesToGib: true,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destinationFilePath))
            {
                File.Delete(destinationFilePath);
                _logger.LogInformation(
                    "Deleted downloaded archive {FileName} after extraction.",
                    Path.GetFileName(destinationFilePath));
            }
        }

        await WaitWhilePausedAsync(pauseController, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            paths.InstalledPath,
            paths.BackupPath,
            _logger);
        PackageStagingFolderCleaner.DeleteEmptyPackageParents(paths.TemporaryPath, _logger);
        _logger.LogInformation("Completed single-file package update.");
    }

    private static ValueTask WaitWhilePausedAsync(
        PackageDownloadPauseController? pauseController,
        CancellationToken cancellationToken)
    {
        return pauseController?.WaitWhilePausedAsync(cancellationToken) ?? ValueTask.CompletedTask;
    }

    /// <summary>
    /// Resolves an HTTP metadata file name as one direct child of the owned staging folder.
    /// </summary>
    private static string ResolveMetadataFilePath(
        string temporaryFolderPath,
        string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidDataException(
                "Remote package metadata did not provide a safe direct file name.");
        }

        string trimmedFileName = fileName.Trim();
        if (trimmedFileName is "." or ".." ||
            trimmedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmedFileName.Contains(Path.DirectorySeparatorChar) ||
            trimmedFileName.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidDataException(
                "Remote package metadata did not provide a safe direct file name.");
        }

        return ManifestPathResolver.ResolvePath(temporaryFolderPath, trimmedFileName);
    }
}
