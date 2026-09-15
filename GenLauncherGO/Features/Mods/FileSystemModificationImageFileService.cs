using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Shared.IO;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Mods;

/// <summary>
///     Manages cached modification image files on disk.
/// </summary>
internal sealed class FileSystemModificationImageFileService : IModificationImageFileService
{
    private readonly ILogger<FileSystemModificationImageFileService> _logger;
    private readonly LauncherRuntimePathContext _runtimePathContext;

    public FileSystemModificationImageFileService(
        LauncherRuntimePathContext runtimePathContext,
        ILogger<FileSystemModificationImageFileService> logger)
    {
        _runtimePathContext = runtimePathContext ?? throw new ArgumentNullException(nameof(runtimePathContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string? FindExistingImageFilePath(
        ModificationType modificationType,
        string modificationName,
        string imageBaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modificationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageBaseName);

        LauncherPaths paths = _runtimePathContext.ActivePaths;
        string? imageDirectory = ResolveExistingImageDirectory(paths, modificationType, modificationName);
        if (imageDirectory is null)
        {
            return null;
        }

        string? imageFilePath = Directory.EnumerateFiles(
                imageDirectory,
                GetImageSearchPattern(paths, modificationType, modificationName, imageBaseName))
            .FirstOrDefault();
        return imageFilePath is null
            ? null
            : ModificationImageCachePath.ResolvePath(paths, imageFilePath);
    }

    public int CountImageFiles(ModificationType modificationType, string modificationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modificationName);

        LauncherPaths paths = _runtimePathContext.ActivePaths;
        string? imageDirectory = ResolveExistingImageDirectory(paths, modificationType, modificationName);
        if (imageDirectory is null)
        {
            return 0;
        }

        return Directory.EnumerateFiles(imageDirectory).Count();
    }

    public bool ImageExists(string? imageFilePath)
    {
        if (string.IsNullOrWhiteSpace(imageFilePath))
        {
            return false;
        }

        try
        {
            return File.Exists(ModificationImageCachePath.ResolvePath(
                _runtimePathContext.ActivePaths,
                imageFilePath));
        }
        catch (Exception exception) when (ModificationCacheFailure.IsRecoverable(exception))
        {
            return false;
        }
    }

    public bool TryDeleteImage(
        ModificationType modificationType,
        string modificationName,
        string imageBaseName)
    {
        try
        {
            LauncherPaths paths = _runtimePathContext.ActivePaths;
            string? imageDirectory = ResolveExistingImageDirectory(paths, modificationType, modificationName);
            if (imageDirectory is null)
            {
                return true;
            }

            string imageSearchPattern = GetImageSearchPattern(
                paths,
                modificationType,
                modificationName,
                imageBaseName);
            foreach (string imageFilePath in Directory.EnumerateFiles(imageDirectory, imageSearchPattern).ToList())
            {
                File.Delete(ModificationImageCachePath.ResolvePath(paths, imageFilePath));
            }

            return true;
        }
        catch (Exception exception) when (ModificationCacheFailure.IsRecoverable(exception))
        {
            _logger.LogWarning(
                exception,
                "Could not remove cached modification image {ImageBaseName} for {ModificationName}.",
                imageBaseName,
                modificationName);
            return false;
        }
    }

    public Task<string> ReplaceImageAsync(
        string modificationName,
        string imageBaseName,
        string sourceImagePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modificationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageBaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceImagePath);

        LauncherPaths paths = _runtimePathContext.ActivePaths;
        return Task.Run(
            () => ReplaceImage(paths, modificationName, imageBaseName, sourceImagePath, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    ///     Replaces the cached image file and removes stale sibling extensions.
    /// </summary>
    private string ReplaceImage(
        LauncherPaths paths,
        string modificationName,
        string imageBaseName,
        string sourceImagePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string extension = Path.GetExtension(sourceImagePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new ArgumentException(
                "The source image must have a file extension.",
                nameof(sourceImagePath));
        }

        string sourcePath = LexicalPath.NormalizeFullPath(sourceImagePath);

        try
        {
            string destinationDirectory = OwnedDirectoryTree.EnsureExists(
                paths.ImagesDirectory,
                ModificationImageCachePath.ResolveDirectory(
                    paths,
                    ModificationType.Mod,
                    modificationName));
            string destinationPath = ModificationImageCachePath.ResolveImagePath(
                paths,
                ModificationType.Mod,
                modificationName,
                imageBaseName + extension);
            FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(
                destinationDirectory,
                "Cached modification image directories");

            if (LexicalPath.AreEquivalent(sourcePath, destinationPath))
            {
                return destinationPath;
            }

            string imageSearchPattern = Path.GetFileNameWithoutExtension(destinationPath) + ".*";
            foreach (string existingImagePath in Directory.EnumerateFiles(destinationDirectory, imageSearchPattern))
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Delete(ModificationImageCachePath.ResolvePath(paths, existingImagePath));
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(sourcePath, destinationPath);
            return destinationPath;
        }
        catch (Exception exception) when (ModificationCacheFailure.IsRecoverable(exception))
        {
            _logger.LogError(
                exception,
                "Could not replace cached modification image {ImageBaseName} for {ModificationName}.",
                imageBaseName,
                modificationName);
            throw new IOException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Could not replace cached image '{0}' for modification '{1}'.",
                    imageBaseName,
                    modificationName),
                exception);
        }
    }

    /// <summary>
    ///     Resolves an existing image directory and rejects linked entries before callers enumerate it.
    /// </summary>
    private static string? ResolveExistingImageDirectory(
        LauncherPaths paths,
        ModificationType modificationType,
        string modificationName)
    {
        string imageDirectory = ModificationImageCachePath.ResolveDirectory(
            paths,
            modificationType,
            modificationName);
        if (!Directory.Exists(imageDirectory))
        {
            return null;
        }

        FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(
            imageDirectory,
            "Cached modification image directories");
        return imageDirectory;
    }

    private static string GetImageSearchPattern(
        LauncherPaths paths,
        ModificationType modificationType,
        string modificationName,
        string imageBaseName)
    {
        string validatedImagePath = ModificationImageCachePath.ResolveImagePath(
            paths,
            modificationType,
            modificationName,
            imageBaseName + ".cache");
        return Path.GetFileNameWithoutExtension(validatedImagePath) + ".*";
    }
}
