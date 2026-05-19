using System;
using System.IO;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;

namespace GenLauncherGO.Infrastructure.Mods.Support;

/// <summary>
///     Owns safe cached-image paths and the file naming convention shared by catalog downloads and integrity repair.
/// </summary>
internal static class ModificationImageCachePath
{
    private const string PathSubject = "Cached modification image paths";

    private const string ImageDirectoryOwnerDescription = "the launcher-owned image directory";

    private const string CacheDirectoryOwnerDescription = "the modification image cache directory";

    public static string ResolveDirectory(LauncherPaths paths, string modificationName)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return ResolvePath(paths, paths.GetModificationImagesDirectory(modificationName));
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

    public static string ResolveRemoteImagePath(
        LauncherPaths paths,
        string modificationName,
        string imageBaseName,
        Uri sourceUri)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return ResolvePath(
            paths,
            paths.GetModificationImageFilePath(
                modificationName,
                GetRemoteImageFileName(imageBaseName, sourceUri)));
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

}
