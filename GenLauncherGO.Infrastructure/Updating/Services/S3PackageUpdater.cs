using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Support;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;

namespace GenLauncherGO.Infrastructure.Updating.Services;

internal sealed class S3PackageUpdater : IS3PackageUpdater
{
    private const int MaxHashAttempts = 3;
    private const int MaxConcurrentFileDownloads = 6;
    private const int PresignedUrlLifetimeHours = 12;

    private readonly IResumableFileDownloader _fileDownloader;
    private readonly IFileHashService _fileHashService;
    private readonly ILogger<S3PackageUpdater> _logger;
    private readonly S3ReusablePackageFileCopier _reusableFileCopier;

    public S3PackageUpdater(
        IResumableFileDownloader fileDownloader,
        IFileHashService fileHashService,
        ILogger<S3PackageUpdater> logger)
    {
        _fileDownloader = fileDownloader ?? throw new ArgumentNullException(nameof(fileDownloader));
        _fileHashService = fileHashService ?? throw new ArgumentNullException(nameof(fileHashService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reusableFileCopier = new S3ReusablePackageFileCopier(_fileHashService, _logger);
    }

    /// <summary>
    /// Downloads missing package files to the temporary folder, reuses unchanged files from the latest installed
    /// version, validates reliable hashes, converts downloaded <c>.big</c> files to <c>.gib</c>, and stages the
    /// temporary folder into the installed package location.
    /// </summary>
    public async Task UpdateAsync(
        S3PackageUpdateRequest request,
        IProgress<PackageUpdateProgress>? progress,
        CancellationToken cancellationToken,
        PackageDownloadPauseController? pauseController = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source.AccessKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source.SecretKey);
        ArgumentNullException.ThrowIfNull(request.PathSet);

        await WaitWhilePausedAsync(pauseController, cancellationToken).ConfigureAwait(false);
        PackageUpdatePathSet ownedPaths = request.PathSet;
        string temporaryFolderPath = ownedPaths.TemporaryPath.FullPath;
        EnsureUniqueManifestDestinations(temporaryFolderPath, request.Files);
        PackageStagingFolderCleaner.RemoveUnsafeLinks(
            ownedPaths.TemporaryPath,
            _logger,
            cancellationToken);
        _logger.LogInformation(
            "Starting S3 package update for bucket {BucketName}, folder {FolderName}; files: {FileCount}.",
            request.Source.BucketName,
            request.Source.Prefix,
            request.Files.Count);

        if (ownedPaths.LatestInstalledPath is not null &&
            Directory.Exists(ownedPaths.LatestInstalledPath.FullPath))
        {
            await _reusableFileCopier.CopyUnchangedFilesAsync(
                ownedPaths.LatestInstalledPath,
                ownedPaths.TemporaryPath,
                request.Files,
                cancellationToken).ConfigureAwait(false);
        }

        await WaitWhilePausedAsync(pauseController, cancellationToken).ConfigureAwait(false);
        await DownloadFilesAsync(
            request.Files,
            request.Source,
            temporaryFolderPath,
            request.HashCheckedExtensions,
            progress,
            pauseController,
            cancellationToken).ConfigureAwait(false);

        await WaitWhilePausedAsync(pauseController, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        PackageStagingFolderCleaner.PruneToManifest(
            ownedPaths.TemporaryPath,
            request.Files,
            _logger,
            cancellationToken);

        await WaitWhilePausedAsync(pauseController, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        PackageInstallFolderReplacer.Replace(
            ownedPaths.TemporaryPath,
            ownedPaths.InstalledPath,
            ownedPaths.BackupPath,
            _logger);
        PackageStagingFolderCleaner.DeleteEmptyPackageParents(ownedPaths.TemporaryPath, _logger);
        _logger.LogInformation(
            "Completed S3 package update for bucket {BucketName}, folder {FolderName}.",
            request.Source.BucketName,
            request.Source.Prefix);
    }

    /// <summary>
    /// Downloads selected S3 manifest files directly into an installed package folder, validating reliable hashes and
    /// preserving unrelated installed files.
    /// </summary>
    public async Task RepairFilesAsync(
        S3PackageFileRepairRequest request,
        IProgress<PackageUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Source);
        ArgumentNullException.ThrowIfNull(request.InstalledPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source.AccessKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source.SecretKey);
        string installedFolderPath = request.InstalledPath.FullPath;

        EnsureUniqueManifestDestinations(installedFolderPath, request.Files);
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            Path.GetDirectoryName(installedFolderPath) ?? request.InstalledPath.OwnerRoot,
            "Installed package paths must be rooted.",
            "Installed package folder contains a linked path and cannot be repaired safely.");
        Directory.CreateDirectory(installedFolderPath);
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            installedFolderPath,
            "Installed package paths must be rooted.",
            "Installed package folder contains a linked path and cannot be repaired safely.");
        FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(
            installedFolderPath,
            "Installed package folder contains a linked path and cannot be repaired safely.");
        _logger.LogInformation(
            "Starting S3 package file repair for bucket {BucketName}, folder {FolderName}; files: {FileCount}.",
            request.Source.BucketName,
            request.Source.Prefix,
            request.Files.Count);

        await DownloadFilesAsync(
            request.Files,
            request.Source,
            installedFolderPath,
            request.HashCheckedExtensions,
            progress,
            pauseController: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Completed S3 package file repair for bucket {BucketName}, folder {FolderName}.",
            request.Source.BucketName,
            request.Source.Prefix);
    }

    /// <summary>
    /// Runs the shared resumable, validated, concurrent S3 transfer lifecycle for an update or in-place repair.
    /// </summary>
    private async Task DownloadFilesAsync(
        IReadOnlyList<RemoteFileManifestEntry> files,
        S3ObjectManifestRequest source,
        string destinationFolderPath,
        IReadOnlySet<string> hashCheckedExtensions,
        IProgress<PackageUpdateProgress>? progress,
        PackageDownloadPauseController? pauseController,
        CancellationToken cancellationToken)
    {
        List<S3DownloadWorkItem> downloadWorkItems = await CreateDownloadWorkItemsAsync(
            files,
            destinationFolderPath,
            hashCheckedExtensions,
            cancellationToken).ConfigureAwait(false);
        long totalDownloadSize = downloadWorkItems.Sum(download => download.BytesToDownload);
        var progressState = new PackageProgressTracker(totalDownloadSize);
        if (downloadWorkItems.Count == 0)
        {
            progress?.Report(new PackageUpdateProgress(0, 0, 100, null));
        }

        using SemaphoreSlim downloadSlots = new(MaxConcurrentFileDownloads);
        IMinioClient client = Clients.MinioClientFactory.Create(
            source.Endpoint,
            source.AccessKey,
            source.SecretKey,
            source.UseSsl);

        var downloadTasks = downloadWorkItems
            .Select(download => DownloadFileWithSlotAsync(
                client,
                source.BucketName,
                source.Prefix,
                destinationFolderPath,
                hashCheckedExtensions,
                download,
                progressState,
                progress,
                downloadSlots,
                pauseController,
                cancellationToken))
            .ToList();

        await Task.WhenAll(downloadTasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the set of manifest files that still require remote transfer after reusable files are staged.
    /// </summary>
    private async Task<List<S3DownloadWorkItem>> CreateDownloadWorkItemsAsync(
        IReadOnlyList<RemoteFileManifestEntry> files,
        string destinationFolderPath,
        IReadOnlySet<string> hashCheckedExtensions,
        CancellationToken cancellationToken)
    {
        List<S3DownloadWorkItem> downloads = new();
        foreach (RemoteFileManifestEntry file in files)
        {
            string destinationFilePath = ManifestPathResolver.ResolvePath(
                destinationFolderPath,
                file.FileName);
            EnsureSafeDestinationPath(destinationFolderPath, destinationFilePath);
            if (await CheckFileSuccessDownloadAsync(
                    file,
                    destinationFilePath,
                    hashCheckedExtensions,
                    cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            BigFileVariantPath.PrepareBigFileResumePath(destinationFilePath);

            long expectedBytes = (long)file.Size;
            long existingBytes = GetExistingBytesForProgress(destinationFilePath);
            if (existingBytes >= expectedBytes)
            {
                DeleteFailedFile(destinationFilePath);
                existingBytes = 0;
            }

            downloads.Add(new S3DownloadWorkItem(
                file,
                Math.Max(0, existingBytes),
                Math.Max(0, expectedBytes - existingBytes)));
        }

        return downloads;
    }

    private static bool ExistingDownloadedFileMatchesExpectedSize(
        RemoteFileManifestEntry file,
        string destinationFilePath)
    {
        string existingFilePath = BigFileVariantPath.GetExistingDownloadedPath(destinationFilePath);
        return !string.IsNullOrWhiteSpace(existingFilePath) &&
               new FileInfo(existingFilePath).Length == (long)file.Size;
    }

    private async Task DownloadFileWithSlotAsync(
        IMinioClient client,
        string bucketName,
        string folderName,
        string destinationFolderPath,
        IReadOnlySet<string> hashCheckedExtensions,
        S3DownloadWorkItem download,
        PackageProgressTracker progressState,
        IProgress<PackageUpdateProgress>? progress,
        SemaphoreSlim downloadSlots,
        PackageDownloadPauseController? pauseController,
        CancellationToken cancellationToken)
    {
        await WaitWhilePausedAsync(pauseController, cancellationToken).ConfigureAwait(false);
        await downloadSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DownloadVerifiedFileAsync(
                client,
                bucketName,
                folderName,
                destinationFolderPath,
                hashCheckedExtensions,
                download,
                progressState,
                progress,
                pauseController,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            downloadSlots.Release();
        }
    }

    /// <summary>
    /// Downloads a file, validates size and hash when available, and retries hash mismatches.
    /// </summary>
    private async Task DownloadVerifiedFileAsync(
        IMinioClient client,
        string bucketName,
        string folderName,
        string destinationFolderPath,
        IReadOnlySet<string> hashCheckedExtensions,
        S3DownloadWorkItem download,
        PackageProgressTracker progressState,
        IProgress<PackageUpdateProgress>? progress,
        PackageDownloadPauseController? pauseController,
        CancellationToken cancellationToken)
    {
        RemoteFileManifestEntry file = download.File;
        string destinationFilePath = ManifestPathResolver.ResolvePath(
            destinationFolderPath,
            file.FileName);
        EnsureSafeDestinationPath(destinationFolderPath, destinationFilePath);

        for (int attempt = 1; attempt <= MaxHashAttempts; attempt++)
        {
            BigFileVariantPath.PrepareBigFileResumePath(destinationFilePath);
            long resumeOffset = attempt == 1 ? download.ExistingBytes : 0;
            long expectedTransferBytes = Math.Max(0, (long)file.Size - resumeOffset);
            string progressItemName = $"{file.FileName}#attempt-{attempt}";
            if (attempt > 1)
            {
                progressState.AddExpectedBytes((long)file.Size);
            }

            Uri downloadUri = await BuildDownloadUriAsync(
                client,
                bucketName,
                folderName,
                file.FileName).ConfigureAwait(false);
            IProgress<DownloadProgress> downloadProgress = new InlineProgress<DownloadProgress>(report =>
            {
                if (resumeOffset > 0 && report.BytesDownloaded < resumeOffset)
                {
                    progressState.AddExpectedBytes(resumeOffset);
                    expectedTransferBytes += resumeOffset;
                    resumeOffset = 0;
                }

                long transferredBytes = Math.Min(
                    Math.Max(0, report.BytesDownloaded - resumeOffset),
                    expectedTransferBytes);
                bool completed = report.TotalBytes.HasValue && report.BytesDownloaded >= report.TotalBytes.Value;
                long verifiedBytes = completed && expectedTransferBytes > 0
                    ? Math.Min(transferredBytes, expectedTransferBytes - 1)
                    : transferredBytes;
                ReportFileProgress(
                    progressState,
                    progress,
                    progressItemName,
                    verifiedBytes,
                    completed);
            });

            await _fileDownloader.DownloadFileAsync(
                new DownloadFileRequest(
                    downloadUri,
                    destinationFilePath,
                    (long)file.Size,
                    Resume: true,
                    PauseController: pauseController),
                downloadProgress,
                cancellationToken).ConfigureAwait(false);

            await WaitWhilePausedAsync(pauseController, cancellationToken).ConfigureAwait(false);
            BigFileVariantPath.ConvertBigFileToGib(destinationFilePath);

            if (await CheckFileSuccessDownloadAsync(
                    file,
                    destinationFilePath,
                    hashCheckedExtensions,
                    cancellationToken).ConfigureAwait(false))
            {
                ReportFileProgress(
                    progressState,
                    progress,
                    progressItemName,
                    expectedTransferBytes,
                    forceReport: true);
                return;
            }

            if (attempt == MaxHashAttempts)
            {
                throw new IOException("Hash sum mismatch detected after repeated download attempts.");
            }

            // Account for the completed but invalid transfer without publishing a false terminal 100% report.
            progressState.CompleteItemSilently(progressItemName, expectedTransferBytes);
            _logger.LogWarning(
                "Hash validation failed for {FileName}; retrying download attempt {NextAttempt}.",
                file.FileName,
                attempt + 1);
            DeleteFailedFile(destinationFilePath);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
    }

    private static ValueTask WaitWhilePausedAsync(
        PackageDownloadPauseController? pauseController,
        CancellationToken cancellationToken)
    {
        return pauseController?.WaitWhilePausedAsync(cancellationToken) ?? ValueTask.CompletedTask;
    }

    private async Task<bool> CheckFileSuccessDownloadAsync(
        RemoteFileManifestEntry file,
        string destinationFilePath,
        IReadOnlySet<string> hashCheckedExtensions,
        CancellationToken cancellationToken)
    {
        string existingFilePath = BigFileVariantPath.GetExistingDownloadedPath(destinationFilePath);
        if (string.IsNullOrWhiteSpace(existingFilePath))
        {
            return false;
        }

        if (!ExistingDownloadedFileMatchesExpectedSize(file, destinationFilePath))
        {
            return false;
        }

        if (!S3HashValidationPolicy.ShouldCheckHash(file, hashCheckedExtensions))
        {
            return true;
        }

        string hashSum = await _fileHashService.ComputeMd5HashAsync(existingFilePath, cancellationToken)
            .ConfigureAwait(false);

        return string.Equals(hashSum, file.Hash, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Uri> BuildDownloadUriAsync(
        IMinioClient client,
        string bucketName,
        string folderName,
        string fileName)
    {
        int expirySeconds = (int)Math.Min(
            TimeSpan.FromHours(PresignedUrlLifetimeHours).TotalSeconds,
            int.MaxValue);
        string objectName = BuildObjectName(folderName, fileName);
        PresignedGetObjectArgs args = new PresignedGetObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectName)
            .WithExpiry(expirySeconds);
        string presignedUrl = await client.PresignedGetObjectAsync(args).ConfigureAwait(false);
        return new Uri(presignedUrl, UriKind.Absolute);
    }

    private static long GetExistingBytesForProgress(string destinationFilePath)
    {
        string existingPath = BigFileVariantPath.GetExistingDownloadedPath(destinationFilePath);
        if (string.IsNullOrWhiteSpace(existingPath))
        {
            return 0;
        }

        return new FileInfo(existingPath).Length;
    }

    /// <summary>
    /// Deletes failed <c>.big</c> and <c>.gib</c> staged variants before retrying a download.
    /// </summary>
    private void DeleteFailedFile(string destinationFilePath)
    {
        if (File.Exists(destinationFilePath))
        {
            File.Delete(destinationFilePath);
            _logger.LogInformation(
                "Deleted failed downloaded file {FileName}.",
                Path.GetFileName(destinationFilePath));
        }

        string gibFilePath = BigFileVariantPath.GetGibVariantPath(destinationFilePath);
        if (File.Exists(gibFilePath))
        {
            File.Delete(gibFilePath);
            _logger.LogInformation(
                "Deleted failed converted file {FileName}.",
                Path.GetFileName(gibFilePath));
        }
    }

    private static string BuildObjectName(string folderName, string fileName)
    {
        string normalizedFolderName = LexicalPath.NormalizeRelativePath(folderName);
        string normalizedFileName = ManifestPathResolver.NormalizeForManifestIndex(fileName);
        if (string.IsNullOrWhiteSpace(normalizedFolderName))
        {
            return normalizedFileName;
        }

        return $"{normalizedFolderName}/{normalizedFileName}";
    }

    private static void ReportFileProgress(
        PackageProgressTracker progressState,
        IProgress<PackageUpdateProgress>? progress,
        string fileName,
        long bytesRead,
        bool forceReport = false)
    {
        PackageUpdateProgress? report = progressState.Update(fileName, bytesRead, forceReport);
        if (report is not null)
        {
            progress?.Report(report);
        }
    }

    /// <summary>
    /// Creates a manifest file's parent inside the package root and rejects linked path segments before file access.
    /// </summary>
    private static void EnsureSafeDestinationPath(
        string packageRoot,
        string destinationFilePath)
    {
        string destinationDirectory = Path.GetDirectoryName(destinationFilePath) ?? packageRoot;
        if (!string.Equals(
            LexicalPath.NormalizeFullPath(packageRoot),
            LexicalPath.NormalizeFullPath(destinationDirectory),
            StringComparison.OrdinalIgnoreCase))
        {
            OwnedDirectoryTree.EnsureExists(packageRoot, destinationDirectory);
        }

        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            destinationFilePath,
            "Package file paths must be rooted.",
            "Package file path contains a linked segment and cannot be updated safely.");
    }

    /// <summary>
    /// Rejects manifest aliases that would write or convert more than one object into the same local file.
    /// </summary>
    private static void EnsureUniqueManifestDestinations(
        string packageRoot,
        IReadOnlyList<RemoteFileManifestEntry> files)
    {
        HashSet<string> destinations = new(StringComparer.OrdinalIgnoreCase);
        foreach (RemoteFileManifestEntry file in files)
        {
            string destinationPath = ManifestPathResolver.ResolvePath(packageRoot, file.FileName);
            destinationPath = BigFileVariantPath.GetInstalledPath(destinationPath);

            if (!destinations.Add(destinationPath))
            {
                throw new InvalidDataException(
                    "The remote package manifest contains duplicate local file destinations.");
            }
        }
    }

    private sealed record S3DownloadWorkItem(
        RemoteFileManifestEntry File,
        long ExistingBytes,
        long BytesToDownload);
}
