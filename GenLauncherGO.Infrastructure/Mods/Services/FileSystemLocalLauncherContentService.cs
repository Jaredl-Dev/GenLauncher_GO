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
///     Performs local file-system operations for launcher-managed mods, patches, add-ons, and cached images.
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
            "Launcher content paths");
        FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(
            paths.ModsDirectory,
            "Launcher content paths");

        foreach (DirectoryInfo contentDirectory in modsDirectory.GetDirectories())
        {
            AddInstalledVersions(contentDirectory, versions);
        }

        return versions;
    }

    public void DeleteEmptyPackageBackupDirectories(LauncherPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (OwnedDirectoryTree.DeleteEmptyDirectories(paths.PackageBackupsDirectory))
        {
            _logger.LogInformation("Deleted the empty launcher package recovery directory.");
        }
    }

    public void DeleteVersion(
        LauncherPaths paths,
        LauncherContentKey contentKey)
    {
        DeleteResolvedContent(
            paths,
            contentKey,
            LauncherContentPathResolver.ResolveVersionPath,
            "Deleted launcher content version");
    }

    public void DeleteContent(
        LauncherPaths paths,
        LauncherContentKey contentKey)
    {
        DeleteResolvedContent(
            paths,
            contentKey,
            LauncherContentPathResolver.ResolveContentPath,
            "Deleted launcher content");
    }

    /// <summary>
    ///     Deletes resolved launcher content together with its resumable staging and durable recovery state.
    /// </summary>
    private void DeleteResolvedContent(
        LauncherPaths paths,
        LauncherContentKey contentKey,
        Func<LauncherPaths, LauncherContentKey, OwnedContentPath?> resolvePath,
        string deletionDescription)
    {
        ArgumentNullException.ThrowIfNull(paths);

        OwnedContentPath? resolvedPath;
        try
        {
            resolvedPath = resolvePath(paths, contentKey);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "Refusing to delete a launcher content path outside the mods root.",
                exception);
        }

        if (resolvedPath is null)
        {
            return;
        }

        bool deletedContent = DeleteDirectoryIfExists(
            resolvedPath,
            deletionDescription,
            contentKey.Name,
            contentKey.Version);

        DeletePackageStagingDirectory(paths, resolvedPath, contentKey);
        DeletePackageBackupDirectory(paths, resolvedPath, contentKey);

        if (!deletedContent)
        {
            return;
        }

        OwnedContentPath? cleanupRoot = LauncherContentPathResolver.ResolveCleanupRootPath(paths, contentKey);
        if (cleanupRoot is not null)
        {
            OwnedDirectoryTree.DeleteEmptyDirectories(cleanupRoot);
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
        bool removedLinkedCache = FileSystemPathSafety.IsReparsePoint(imageFolderPath);
        if (!OwnedDirectoryTree.DeleteIfExists(ownedImagePath))
        {
            return;
        }

        if (removedLinkedCache)
        {
            _logger.LogWarning(
                "Removed linked modification image cache folder {ImageFolderName} without traversing its target.",
                Path.GetFileName(imageFolderPath));
            return;
        }

        _logger.LogInformation(
            "Deleted unused modification image cache folder {ImageFolderName}.",
            Path.GetFileName(imageFolderPath));
    }

    private static void AddInstalledVersions(
        DirectoryInfo contentDirectory,
        List<LauncherContentVersion> versions)
    {
        foreach (DirectoryInfo subDirectory in contentDirectory.GetDirectories())
        {
            ModificationType? childContentType = null;
            if (string.Equals(
                    subDirectory.Name,
                    LauncherFileSystemLayout.AddonsFolderName,
                    StringComparison.OrdinalIgnoreCase))
            {
                childContentType = ModificationType.Addon;
            }
            else if (string.Equals(
                         subDirectory.Name,
                         LauncherFileSystemLayout.PatchesFolderName,
                         StringComparison.OrdinalIgnoreCase))
            {
                childContentType = ModificationType.Patch;
            }

            if (childContentType.HasValue)
            {
                foreach (DirectoryInfo childDirectory in subDirectory.GetDirectories())
                {
                    AddInstalledChildVersions(
                        childDirectory,
                        contentDirectory.Name,
                        childContentType.Value,
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
    ///     Deletes the temporary package staging directory for a content version when it exists.
    /// </summary>
    private void DeletePackageStagingDirectory(
        LauncherPaths paths,
        OwnedContentPath versionPath,
        LauncherContentKey contentKey)
    {
        OwnedContentPath packageStagingPath = paths.GetPackageTemporaryPath(versionPath);

        DeleteDirectoryIfExists(
            packageStagingPath,
            "Deleted temporary launcher package staging folder for",
            contentKey.Name,
            contentKey.Version);
        DeleteEmptyPackageStagingParents(packageStagingPath);
    }

    /// <summary>
    ///     Deletes the durable recovery backup for content the user deliberately removed.
    /// </summary>
    private void DeletePackageBackupDirectory(
        LauncherPaths paths,
        OwnedContentPath installedPath,
        LauncherContentKey contentKey)
    {
        OwnedContentPath packageBackupPath = paths.GetPackageBackupPath(installedPath);

        DeleteDirectoryIfExists(
            packageBackupPath,
            "Deleted obsolete launcher package recovery backup for",
            contentKey.Name,
            contentKey.Version);
        DeleteEmptyPackageBackupParents(packageBackupPath);
    }

    /// <summary>
    ///     Deletes empty package staging parent directories without crossing outside the package staging root.
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

    /// <summary>
    ///     Deletes empty recovery-backup parent directories without crossing outside the package backup root.
    /// </summary>
    private void DeleteEmptyPackageBackupParents(OwnedContentPath packageBackupPath)
    {
        foreach (string deletedDirectory in OwnedDirectoryTree.DeleteEmptyParentsIncludingRoot(
                     packageBackupPath.OwnerRoot,
                     packageBackupPath.FullPath))
        {
            _logger.LogInformation(
                "Deleted empty launcher package recovery folder {BackupFolderName}.",
                Path.GetFileName(deletedDirectory));
        }
    }

    private bool DeleteDirectoryIfExists(
        OwnedContentPath ownedPath,
        string deletionDescription,
        string contentName,
        string contentVersion)
    {
        if (!OwnedDirectoryTree.DeleteIfExists(ownedPath))
        {
            return false;
        }

        // The template stays constant so structured sinks can group these; the part
        // that varies per caller is carried as a property rather than in the template.
        _logger.LogInformation(
            "{DeletionDescription} {ContentName} {ContentVersion}.",
            deletionDescription,
            contentName,
            contentVersion);
        return true;
    }

    private static bool ContentCardExists(LauncherData launcherData, string contentName)
    {
        return launcherData.AllContent
            .Any(entry => entry.ContentKey.HasName(contentName));
    }

}
