using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Mods.Services;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Mods.Contracts;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Mods.Services;

/// <summary>
/// Performs local file-system operations for launcher-managed mods, patches, add-ons, and cached images.
/// </summary>
internal sealed class FileSystemLocalLauncherContentService : ILocalLauncherContentService
{
    private readonly ILogger<FileSystemLocalLauncherContentService> _logger;

    public FileSystemLocalLauncherContentService(ILogger<FileSystemLocalLauncherContentService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<LauncherContentVersion> FindInstalledVersions(LauncherPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var versions = new List<LauncherContentVersion>();
        var modsDirectory = new DirectoryInfo(paths.ModsDirectory);
        if (!modsDirectory.Exists)
        {
            return versions;
        }

        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            paths.ModsDirectory,
            "Launcher content paths must be rooted.",
            "Launcher content paths must not contain reparse points.");
        FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(
            paths.ModsDirectory,
            "Launcher content paths must not contain reparse points.");

        foreach (DirectoryInfo contentDirectory in modsDirectory.GetDirectories())
        {
            AddInstalledVersions(contentDirectory, versions);
        }

        return versions;
    }

    public void DeleteVersion(
        LauncherPaths paths,
        LauncherContentKey contentKey)
    {
        ArgumentNullException.ThrowIfNull(paths);

        OwnedContentPath? versionPath;
        try
        {
            versionPath = LauncherContentPathResolver.ResolveVersionPath(paths, contentKey);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "Refusing to delete a launcher content path outside the mods root.",
                exception);
        }

        if (versionPath is null)
        {
            return;
        }

        bool deletedInstalledVersion = DeleteDirectoryIfExists(
            versionPath,
            "Deleted launcher content version {ContentName} {ContentVersion}.",
            contentKey.Name,
            contentKey.Version);

        DeletePackageStagingDirectory(paths, versionPath, contentKey);

        if (deletedInstalledVersion)
        {
            OwnedContentPath? cleanupRoot = LauncherContentPathResolver.ResolveCleanupRootPath(paths, contentKey);
            if (cleanupRoot is not null)
            {
                OwnedDirectoryTree.DeleteEmptyDirectories(cleanupRoot);
            }
        }
    }

    public void DeleteContent(
        LauncherPaths paths,
        LauncherContentKey contentKey)
    {
        ArgumentNullException.ThrowIfNull(paths);

        OwnedContentPath? contentPath;
        try
        {
            contentPath = LauncherContentPathResolver.ResolveContentPath(paths, contentKey);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "Refusing to delete a launcher content path outside the mods root.",
                exception);
        }

        if (contentPath is null)
        {
            return;
        }

        bool deletedContent = DeleteDirectoryIfExists(
            contentPath,
            "Deleted launcher content {ContentName} {ContentVersion}.",
            contentKey.Name,
            contentKey.Version);

        DeletePackageStagingDirectory(paths, contentPath, contentKey);

        if (deletedContent)
        {
            OwnedContentPath? cleanupRoot = LauncherContentPathResolver.ResolveCleanupRootPath(paths, contentKey);
            if (cleanupRoot is not null)
            {
                OwnedDirectoryTree.DeleteEmptyDirectories(cleanupRoot);
            }
        }
    }

    public void DeleteImagesIfUnused(
        LauncherPaths paths,
        LauncherContentKey contentKey,
        LauncherData launcherData)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(launcherData);

        if (string.IsNullOrWhiteSpace(contentKey.Name) ||
            string.IsNullOrWhiteSpace(contentKey.Version) ||
            ContentCardExists(launcherData, contentKey.Name))
        {
            return;
        }

        string imageFolderPath = paths.GetModificationImagesDirectory(contentKey.Name);
        if (!Directory.Exists(imageFolderPath))
        {
            return;
        }

        var ownedImagePath = new OwnedContentPath(paths.ImagesDirectory, imageFolderPath);
        if (FileSystemPathSafety.IsReparsePoint(imageFolderPath))
        {
            OwnedDirectoryTree.DeleteIfExists(ownedImagePath);
            _logger.LogWarning(
                "Removed linked modification image cache folder {ImageFolderName} without traversing its target.",
                Path.GetFileName(imageFolderPath));
            return;
        }

        EnsureOwnedContentTreeIsSafe(ownedImagePath);
        DeleteImageFiles(imageFolderPath, contentKey.Version);
        DeleteImageFiles(imageFolderPath, contentKey.Version + "-background");
        DeleteImageFolderIfEmpty(imageFolderPath);
    }

    private static void AddInstalledVersions(
        DirectoryInfo contentDirectory,
        List<LauncherContentVersion> versions)
    {
        foreach (DirectoryInfo subDirectory in contentDirectory.GetDirectories())
        {
            if (String.Equals(
                    subDirectory.Name,
                    LauncherFileSystemLayout.AddonsFolderName,
                    StringComparison.OrdinalIgnoreCase))
            {
                foreach (DirectoryInfo addonDirectory in subDirectory.GetDirectories())
                {
                    AddInstalledChildVersions(
                        addonDirectory,
                        contentDirectory.Name,
                        ModificationType.Addon,
                        versions);
                }

                continue;
            }

            if (String.Equals(
                    subDirectory.Name,
                    LauncherFileSystemLayout.PatchesFolderName,
                    StringComparison.OrdinalIgnoreCase))
            {
                foreach (DirectoryInfo patchDirectory in subDirectory.GetDirectories())
                {
                    AddInstalledChildVersions(
                        patchDirectory,
                        contentDirectory.Name,
                        ModificationType.Patch,
                        versions);
                }

                continue;
            }

            if (IsInstallVersionDirectory(subDirectory))
            {
                versions.Add(new LauncherContentVersion(new LauncherContentInstallation
                {
                    Installed = true
                })
                {
                    ModificationType = ModificationType.Mod,
                    Name = contentDirectory.Name,
                    Version = subDirectory.Name
                });
            }
        }
    }

    private static void AddInstalledChildVersions(
        DirectoryInfo contentDirectory,
        string parentContentName,
        ModificationType contentType,
        List<LauncherContentVersion> versions)
    {
        foreach (DirectoryInfo versionDirectory in contentDirectory.GetDirectories())
        {
            if (!IsInstallVersionDirectory(versionDirectory))
            {
                continue;
            }

            versions.Add(new LauncherContentVersion(new LauncherContentInstallation
            {
                Installed = true
            })
            {
                ModificationType = contentType,
                Name = contentDirectory.Name,
                Version = versionDirectory.Name,
                ParentContentName = parentContentName
            });
        }
    }

    private static bool IsInstallVersionDirectory(DirectoryInfo directory)
    {
        return directory.EnumerateFiles("*", SearchOption.AllDirectories).Any();
    }

    /// <summary>
    /// Deletes the temporary package staging directory for a content version when it exists.
    /// </summary>
    private void DeletePackageStagingDirectory(
        LauncherPaths paths,
        OwnedContentPath versionPath,
        LauncherContentKey contentKey)
    {
        OwnedContentPath packageStagingPath = paths.GetPackageTemporaryPath(versionPath);

        DeleteDirectoryIfExists(
            packageStagingPath,
            "Deleted temporary launcher package staging folder for {ContentName} {ContentVersion}.",
            contentKey.Name,
            contentKey.Version);
        DeleteEmptyPackageStagingParents(packageStagingPath);
    }

    /// <summary>
    /// Deletes empty package staging parent directories without crossing outside the package staging root.
    /// </summary>
    private void DeleteEmptyPackageStagingParents(OwnedContentPath packageStagingPath)
    {
        foreach (string deletedDirectory in OwnedDirectoryTree.DeleteEmptyParents(
                     packageStagingPath.OwnerRoot,
                     packageStagingPath.FullPath))
        {
            _logger.LogInformation(
                "Deleted empty temporary launcher package staging folder {StagingFolderName}.",
                Path.GetFileName(deletedDirectory));
        }
    }

    private bool DeleteDirectoryIfExists(
        OwnedContentPath ownedPath,
        string logMessage,
        string contentName,
        string contentVersion)
    {
        if (!OwnedDirectoryTree.DeleteIfExists(ownedPath))
        {
            return false;
        }

        _logger.LogInformation(logMessage, contentName, contentVersion);
        return true;
    }

    /// <summary>
    /// Rejects reparse points in an owned content path and its existing directory tree before traversal.
    /// </summary>
    private static void EnsureOwnedContentTreeIsSafe(OwnedContentPath ownedPath)
    {
        FileSystemPathSafety.ResolveOwnedSubpath(
            ownedPath.OwnerRoot,
            ownedPath.FullPath,
            "Launcher content paths must remain below their owning root.",
            "Launcher content paths must not contain reparse points.");
        FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(
            ownedPath.FullPath,
            "Launcher content paths must not contain reparse points.");
    }

    private static bool ContentCardExists(LauncherData launcherData, string contentName)
    {
        return ContainsContentName(launcherData.Modifications, contentName) ||
               ContainsContentName(launcherData.Addons, contentName) ||
               ContainsContentName(launcherData.Patches, contentName);
    }

    private static bool ContainsContentName(IEnumerable<LauncherContent> entries, string contentName)
    {
        return entries.Any(entry => entry.ContentKey.HasName(contentName));
    }

    private void DeleteImageFiles(string imageFolderPath, string imageBaseName)
    {
        foreach (string imageFilePath in Directory.EnumerateFiles(imageFolderPath, imageBaseName + ".*"))
        {
            try
            {
                File.Delete(imageFilePath);
                _logger.LogInformation(
                    "Deleted cached modification image {ImageFileName}.",
                    Path.GetFileName(imageFilePath));
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Failed to delete cached modification image {ImageFileName}.",
                    Path.GetFileName(imageFilePath));
            }
        }
    }

    private void DeleteImageFolderIfEmpty(string imageFolderPath)
    {
        try
        {
            if (!Directory.EnumerateFileSystemEntries(imageFolderPath).Any())
            {
                Directory.Delete(imageFolderPath);
                _logger.LogInformation(
                    "Deleted empty modification image cache folder {ImageFolderName}.",
                    Path.GetFileName(imageFolderPath));
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to delete empty modification image cache folder {ImageFolderName}.",
                Path.GetFileName(imageFolderPath));
        }
    }
}
