using System;
using System.Collections.Generic;
using System.IO;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Shared.IO;

namespace GenLauncherGO.Features.Mods;

/// <summary>
///     Owns safe cached-image paths and the file naming convention shared by catalog downloads and integrity repair.
/// </summary>
internal static class ModificationImageCachePath
{
    private const string PathSubject = "Cached modification image paths";

    private const string ImageDirectoryOwnerDescription = "the launcher-owned image directory";

    private const string CacheDirectoryOwnerDescription = "the modification image cache directory";

    public static string ResolveDirectory(
        LauncherPaths paths,
        ModificationType modificationType,
        string modificationName)
    {
        return ResolvePath(paths, GetDirectoryPath(paths, modificationType, modificationName));
    }

    /// <summary>
    ///     Builds the lexical cache path without traversing it, so cleanup can safely unlink a reparse-point entry.
    /// </summary>
    public static string GetDirectoryPath(
        LauncherPaths paths,
        ModificationType modificationType,
        string modificationName)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return paths.GetModificationImagesDirectory(GetDirectoryName(modificationType, modificationName));
    }

    public static string ResolvePath(LauncherPaths paths, string imagePath)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return FileSystemPathSafety.ResolveOwnedSubpath(
            paths.ImagesDirectory,
            imagePath,
            PathSubject,
            ImageDirectoryOwnerDescription);
    }

    public static string ResolveImagePath(
        LauncherPaths paths,
        ModificationType modificationType,
        string modificationName,
        string imageFileName)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return ResolvePath(
            paths,
            paths.GetModificationImageFilePath(
                GetDirectoryName(modificationType, modificationName),
                imageFileName));
    }

    public static string ResolveRemoteImagePath(
        LauncherPaths paths,
        ModificationType modificationType,
        string modificationName,
        string imageBaseName,
        Uri sourceUri)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return ResolveImagePath(
            paths,
            modificationType,
            modificationName,
            GetRemoteImageFileName(imageBaseName, sourceUri));
    }

    public static string ResolveRemoteImagePath(
        string cacheDirectory,
        string imageBaseName,
        Uri sourceUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);

        return FileSystemPathSafety.ResolveOwnedSubpath(
            cacheDirectory,
            Path.Combine(cacheDirectory, GetRemoteImageFileName(imageBaseName, sourceUri)),
            PathSubject,
            CacheDirectoryOwnerDescription);
    }

    /// <summary>
    ///     Enumerates the remote artwork published by one content version and the canonical base name of each asset.
    /// </summary>
    public static IReadOnlyList<(string Link, string BaseName, bool RequiresTheme)> GetRemoteAssetSources(
        LauncherContentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        var sources = new List<(string Link, string BaseName, bool RequiresTheme)>(2);
        if (!string.IsNullOrEmpty(version.UIImageSourceLink))
        {
            sources.Add((version.UIImageSourceLink, version.Version, false));
        }

        string backgroundLink = version.Theme?.GenLauncherBackgroundImageLink ?? string.Empty;
        if (backgroundLink.Length > 0)
        {
            sources.Add((
                backgroundLink,
                LauncherContentTheme.ResolveBackgroundImageBaseName(version.Version),
                true));
        }

        return sources;
    }

    /// <summary>
    ///     Resolves the valid remote artwork destinations expected inside one modification cache directory.
    /// </summary>
    public static IReadOnlyList<(Uri SourceUri, string DestinationPath)> ResolveRemoteAssets(
        LauncherContentVersion version,
        string cacheDirectory)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);

        var assets = new List<(Uri SourceUri, string DestinationPath)>(2);
        foreach ((string link, string baseName, _) in GetRemoteAssetSources(version))
        {
            if (Uri.TryCreate(link, UriKind.Absolute, out Uri? sourceUri))
            {
                assets.Add((sourceUri, ResolveRemoteImagePath(cacheDirectory, baseName, sourceUri)));
            }
        }

        return assets;
    }

    /// <summary>
    ///     Gets every cache-file base name owned by one version, including local palette data and optional artwork.
    /// </summary>
    public static IReadOnlyList<string> GetOwnedBaseNames(LauncherContentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return
        [
            version.Version,
            LauncherContentTheme.ResolveBackgroundImageBaseName(version.Version),
            LauncherContentTheme.ResolveCacheBaseName(version.Version)
        ];
    }

    private static string GetRemoteImageFileName(string imageBaseName, Uri sourceUri)
    {
        ArgumentNullException.ThrowIfNull(sourceUri);

        string extension = Path.GetExtension(sourceUri.LocalPath);
        if (!LauncherContentFileTypes.IsImage(extension))
        {
            extension = LauncherContentFileTypes.DefaultImageExtension;
        }

        return imageBaseName + extension;
    }

    private static string GetDirectoryName(ModificationType modificationType, string modificationName)
    {
        return modificationType == ModificationType.Advertising
            ? modificationName.Trim(Path.GetInvalidFileNameChars())
            : modificationName;
    }
}
