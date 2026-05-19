using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Mods.Models;
using GenLauncherGO.Infrastructure.Mods.Support;
using GenLauncherGO.Infrastructure.Remote.Contracts;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Mods.Services;

/// <summary>
/// Caches remote launcher catalog images on disk.
/// </summary>
internal sealed class LauncherCatalogImageCache
{
    private readonly IRemoteAssetDownloader _assetDownloader;

    private readonly ILogger<LauncherCatalogImageCache> _logger;

    public LauncherCatalogImageCache(
        IRemoteAssetDownloader assetDownloader,
        ILogger<LauncherCatalogImageCache> logger)
    {
        _assetDownloader = assetDownloader ?? throw new ArgumentNullException(nameof(assetDownloader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task CacheModificationImagesAsync(
        LauncherContentVersion modification,
        LauncherPaths paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modification);
        ArgumentNullException.ThrowIfNull(paths);

        if (String.IsNullOrEmpty(modification.UIImageSourceLink))
        {
            return;
        }

        await DownloadImageIfMissingAsync(
            paths,
            modification.Name,
            modification.Version,
            modification.UIImageSourceLink,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task CacheAdvertisingImagesAsync(
        RemoteAdvertisingReference advertising,
        LauncherPaths paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(advertising);
        ArgumentNullException.ThrowIfNull(paths);

        RemoveStaleAdvertisingImages(advertising, paths);

        var imageDownloads = new List<Task>(advertising.ImageUrls.Count);
        int imageIndex = 0;
        foreach (string imageLink in advertising.ImageUrls)
        {
            int currentImageIndex = imageIndex;
            imageDownloads.Add(DownloadImageIfMissingAsync(
                paths,
                advertising.Name,
                currentImageIndex.ToString(),
                imageLink,
                cancellationToken));
            imageIndex++;
        }

        await Task.WhenAll(imageDownloads).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes stale advertising image files when the remote image count changes.
    /// </summary>
    private void RemoveStaleAdvertisingImages(RemoteAdvertisingReference advertising, LauncherPaths paths)
    {
        string folderName = advertising.Name.Trim(Path.GetInvalidFileNameChars());
        try
        {
            string imageFolderPath = ModificationImageCachePath.ResolveDirectory(paths, folderName);
            if (!Directory.Exists(imageFolderPath))
            {
                return;
            }

            FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(
                imageFolderPath,
                "Cached catalog image directories must not contain reparse points.");
            var dirInfo = new DirectoryInfo(imageFolderPath);
            FileInfo[] images = dirInfo.GetFiles();
            if (images.Length == advertising.ImageUrls.Count)
            {
                return;
            }

            foreach (FileInfo image in images)
            {
                try
                {
                    File.Delete(ModificationImageCachePath.ResolvePath(paths, image.FullName));
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Failed to delete stale advertising image {ImageFileName}.",
                        image.Name);
                }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException
                                              or UnauthorizedAccessException or ArgumentException
                                              or NotSupportedException)
        {
            _logger.LogWarning(
                exception,
                "Skipped stale advertising image cleanup for {ModificationName} because its cache path was unavailable.",
                advertising.Name);
        }
    }

    private async Task DownloadImageIfMissingAsync(
        LauncherPaths paths,
        string modificationName,
        string fileName,
        string link,
        CancellationToken cancellationToken)
    {
        try
        {
            var sourceUri = new Uri(link, UriKind.Absolute);
            string imageDirectory = OwnedDirectoryTree.EnsureExists(
                paths.ImagesDirectory,
                ModificationImageCachePath.ResolveDirectory(paths, modificationName));
            FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(
                imageDirectory,
                "Cached catalog image directories must not contain reparse points.");
            string destinationFilePath = ModificationImageCachePath.ResolveRemoteImagePath(
                paths,
                modificationName,
                fileName,
                sourceUri);
            await _assetDownloader.DownloadIfMissingAsync(
                sourceUri,
                destinationFilePath,
                cancellationToken).ConfigureAwait(false);
            FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(
                imageDirectory,
                "Cached catalog image directories must not contain reparse points.");
            _ = ModificationImageCachePath.ResolvePath(paths, destinationFilePath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to download cached image {ImageName} for {ModificationName}.",
                fileName,
                modificationName);
        }
    }
}
