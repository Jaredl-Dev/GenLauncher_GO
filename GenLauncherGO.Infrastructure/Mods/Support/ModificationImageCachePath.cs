using System;
using System.IO;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;

namespace GenLauncherGO.Infrastructure.Mods.Support;

/// <summary>
/// Owns safe cached-image paths and the file naming convention shared by catalog downloads and integrity repair.
/// </summary>
internal static class ModificationImageCachePath
{
    private const string ContainmentFailure =
        "Cached modification image paths must stay inside the launcher-owned image directory.";

    private const string ReparsePointFailure =
        "Cached modification image paths must not contain reparse points.";

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
            ContainmentFailure,
            ReparsePointFailure);
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
            ContainmentFailure,
            ReparsePointFailure);
    }

    private static string GetRemoteImageFileName(string imageBaseName, Uri sourceUri)
    {
        ArgumentNullException.ThrowIfNull(sourceUri);

        string extension = Path.GetExtension(sourceUri.LocalPath);
        if (!IsSupportedImageExtension(extension))
        {
            extension = ".png";
        }

        return imageBaseName + extension;
    }

    private static bool IsSupportedImageExtension(string extension)
    {
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
    }
}
