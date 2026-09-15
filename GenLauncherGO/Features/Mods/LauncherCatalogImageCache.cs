using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Shared.IO;
using GenLauncherGO.Shared.Remote;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Mods;

/// <summary>
///     Caches the assets a remote launcher catalog publishes: tile art, shell artwork, and palettes.
/// </summary>
/// <remarks>
///     Everything cached here shares one lifetime, because it all lands in the modification's own cache folder and is
///     removed with the content card.
/// </remarks>
internal sealed class LauncherCatalogImageCache
{
    private readonly IRemoteAssetDownloader _assetDownloader;

    private readonly ILogger<LauncherCatalogImageCache> _logger;

    private readonly IModificationThemeCache _themeCache;

    public LauncherCatalogImageCache(
        IRemoteAssetDownloader assetDownloader,
        IModificationThemeCache themeCache,
        ILogger<LauncherCatalogImageCache> logger)
    {
        _assetDownloader = assetDownloader ?? throw new ArgumentNullException(nameof(assetDownloader));
        _themeCache = themeCache ?? throw new ArgumentNullException(nameof(themeCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task CacheModificationImagesAsync(
        LauncherContentVersion modification,
        LauncherPaths paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modification);
        ArgumentNullException.ThrowIfNull(paths);

        bool themeSaved = false;
        foreach ((string link, string baseName, bool requiresTheme) in
                 ModificationImageCachePath.GetRemoteAssetSources(modification))
        {
            if (requiresTheme && !themeSaved)
            {
                SaveTheme(modification);
                themeSaved = true;
            }

            await DownloadImageIfMissingAsync(
                paths,
                modification.ModificationType,
                modification.Name,
                baseName,
                link,
                cancellationToken).ConfigureAwait(false);
        }

        if (modification.Theme is not null && !themeSaved)
        {
            SaveTheme(modification);
        }
    }

    private void SaveTheme(LauncherContentVersion modification)
    {
        // Cached ahead of selection so a themed modification can re-skin the shell the moment it is picked, and
        // so a later offline run still has both the palette and the artwork it refers to.
        _themeCache.Save(modification.ContentKey, modification.Theme!);
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
                ModificationType.Advertising,
                advertising.Name,
                currentImageIndex.ToString(CultureInfo.CurrentCulture),
                imageLink,
                cancellationToken));
            imageIndex++;
        }

        await Task.WhenAll(imageDownloads).ConfigureAwait(false);
    }

    /// <summary>
    ///     Removes stale advertising image files when the remote image count changes.
    /// </summary>
    private void RemoveStaleAdvertisingImages(
        RemoteAdvertisingReference advertising,
        LauncherPaths paths)
    {
        try
        {
            string imageFolderPath = ModificationImageCachePath.ResolveDirectory(
                paths,
                ModificationType.Advertising,
                advertising.Name);
            if (!Directory.Exists(imageFolderPath))
            {
                return;
            }

            FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(
                imageFolderPath,
                "Cached catalog image directories");
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
        catch (Exception exception) when (ModificationCacheFailure.IsRecoverable(exception))
        {
            _logger.LogWarning(
                exception,
                "Skipped stale advertising image cleanup for {ModificationName} because its cache path was unavailable.",
                advertising.Name);
        }
    }

    private async Task DownloadImageIfMissingAsync(
        LauncherPaths paths,
        ModificationType modificationType,
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
                ModificationImageCachePath.ResolveDirectory(paths, modificationType, modificationName));
            FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(
                imageDirectory,
                "Cached catalog image directories");
            string destinationFilePath = ModificationImageCachePath.ResolveRemoteImagePath(
                paths,
                modificationType,
                modificationName,
                fileName,
                sourceUri);
            await _assetDownloader.DownloadIfMissingAsync(
                sourceUri,
                destinationFilePath,
                cancellationToken).ConfigureAwait(false);
            FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(
                imageDirectory,
                "Cached catalog image directories");
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
