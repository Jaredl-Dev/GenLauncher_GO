using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Mods.Support;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Mods.Services;

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

    public string? FindExistingImageFilePath(string modificationName, string imageBaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modificationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageBaseName);

        LauncherPaths paths = _runtimePathContext.ActivePaths;
        string? imageDirectory = ResolveExistingImageDirectory(paths, modificationName);
        if (imageDirectory is null)
        {
            return null;
        }

        string? imageFilePath = Directory.EnumerateFiles(
                imageDirectory,
                GetImageSearchPattern(paths, modificationName, imageBaseName))
            .FirstOrDefault();
        return imageFilePath is null
            ? null
            : ModificationImageCachePath.ResolvePath(paths, imageFilePath);
    }

    public int CountImageFiles(string modificationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modificationName);

        LauncherPaths paths = _runtimePathContext.ActivePaths;
        string? imageDirectory = ResolveExistingImageDirectory(paths, modificationName);
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
        catch (Exception exception) when (exception is InvalidDataException or IOException
                                              or UnauthorizedAccessException or ArgumentException
                                              or NotSupportedException)
        {
            return false;
        }
    }

    public bool TryDeleteImage(string modificationName, string imageBaseName)
    {
        try
        {
            LauncherPaths paths = _runtimePathContext.ActivePaths;
            string? imageDirectory = ResolveExistingImageDirectory(paths, modificationName);
            if (imageDirectory is null)
            {
                return true;
            }

            string imageSearchPattern = GetImageSearchPattern(paths, modificationName, imageBaseName);
            foreach (string imageFilePath in Directory.EnumerateFiles(imageDirectory, imageSearchPattern).ToList())
            {
                File.Delete(ModificationImageCachePath.ResolvePath(paths, imageFilePath));
            }

            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException
                                              or UnauthorizedAccessException or ArgumentException
                                              or NotSupportedException)
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
        ModificationImageReplacementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        LauncherPaths paths = _runtimePathContext.ActivePaths;
        return Task.Run(() => ReplaceImage(paths, request, cancellationToken), cancellationToken);
    }

    /// <summary>
    ///     Replaces the cached image file and removes stale sibling extensions.
    /// </summary>
    private string ReplaceImage(
        LauncherPaths paths,
        ModificationImageReplacementRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string extension = Path.GetExtension(request.SourceImagePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new ArgumentException(
                "The source image must have a file extension.",
                nameof(request));
        }

        string sourcePath = LexicalPath.NormalizeFullPath(request.SourceImagePath);

        try
        {
            string destinationDirectory = OwnedDirectoryTree.EnsureExists(
                paths.ImagesDirectory,
                ModificationImageCachePath.ResolveDirectory(paths, request.ModificationName));
            string destinationPath = ModificationImageCachePath.ResolvePath(
                paths,
                paths.GetModificationImageFilePath(
                    request.ModificationName,
                    request.ImageBaseName + extension));
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
        catch (Exception exception) when (exception is InvalidDataException or IOException
                                              or UnauthorizedAccessException or ArgumentException
                                              or NotSupportedException)
        {
            _logger.LogError(
                exception,
                "Could not replace cached modification image {ImageBaseName} for {ModificationName}.",
                request.ImageBaseName,
                request.ModificationName);
            throw new IOException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Could not replace cached image '{0}' for modification '{1}'.",
                    request.ImageBaseName,
                    request.ModificationName),
                exception);
        }
    }

    /// <summary>
    ///     Resolves an existing image directory and rejects linked entries before callers enumerate it.
    /// </summary>
    private static string? ResolveExistingImageDirectory(
        LauncherPaths paths,
        string modificationName)
    {
        string imageDirectory = ModificationImageCachePath.ResolveDirectory(paths, modificationName);
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
        string modificationName,
        string imageBaseName)
    {
        string validatedImagePath = paths.GetModificationImageFilePath(
            modificationName,
            imageBaseName + ".cache");
        return Path.GetFileNameWithoutExtension(validatedImagePath) + ".*";
    }
}
