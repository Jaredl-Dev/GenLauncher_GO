using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Updating.Models;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Updating.Support;

/// <summary>
///     Cleans package staging folders before they are moved into an installed package location.
/// </summary>
internal static class PackageStagingFolderCleaner
{
    /// <summary>
    ///     Deletes all child entries from an explicitly launcher-owned staging folder.
    /// </summary>
    public static void ClearDirectory(OwnedContentPath stagingPath, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(stagingPath);
        ArgumentNullException.ThrowIfNull(logger);

        string stagingRoot = OwnedDirectoryTree.PrepareEmpty(stagingPath.OwnerRoot, stagingPath.FullPath);

        logger.LogInformation(
            "Cleared package staging folder {StagingFolderName}.",
            Path.GetFileName(stagingRoot));
    }

    /// <summary>
    ///     Deletes empty package staging parent folders after a staged package version folder has been moved into place.
    /// </summary>
    public static void DeleteEmptyPackageParents(OwnedContentPath stagingPath, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(stagingPath);
        ArgumentNullException.ThrowIfNull(logger);

        var packagesDirectory = new DirectoryInfo(stagingPath.OwnerRoot);
        if (!string.Equals(
                packagesDirectory.Name,
                LauncherFileSystemLayout.PackagesFolderName,
                StringComparison.OrdinalIgnoreCase) ||
            packagesDirectory.Parent is null)
        {
            return;
        }

        try
        {
            foreach (string deletedDirectory in OwnedDirectoryTree.DeleteEmptyParents(
                         packagesDirectory.Parent.FullName,
                         stagingPath.FullPath))
            {
                logger.LogInformation(
                    "Deleted empty package staging folder {StagingFolderName}.",
                    Path.GetFileName(deletedDirectory));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "Failed to delete empty package staging parents for {StagingFolderName}.",
                Path.GetFileName(stagingPath.FullPath));
        }
    }

    /// <summary>
    ///     Deletes reparse points from an explicitly launcher-owned staging folder without following them.
    /// </summary>
    public static void RemoveUnsafeLinks(
        OwnedContentPath stagingPath,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stagingPath);
        ArgumentNullException.ThrowIfNull(logger);

        bool replacedRootLink = Directory.Exists(stagingPath.FullPath) &&
                                FileSystemPathSafety.IsReparsePoint(stagingPath.FullPath);
        string stagingRoot = OwnedDirectoryTree.EnsureRealDirectory(
            stagingPath.OwnerRoot,
            stagingPath.FullPath);
        if (replacedRootLink)
        {
            logger.LogWarning(
                "Removed unsafe staging-root link {StagingFolderName}.",
                Path.GetFileName(stagingRoot));
        }

        cancellationToken.ThrowIfCancellationRequested();
        foreach (string deletedPath in OwnedDirectoryTree.DeleteReparsePoints(stagingPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogWarning(
                "Removed unsafe staging link {EntryName}.",
                Path.GetFileName(deletedPath));
        }
    }

    /// <summary>
    ///     Deletes staged files that are not expected by the remote manifest from an explicitly owned staging path.
    /// </summary>
    public static void PruneToManifest(
        OwnedContentPath stagingPath,
        IReadOnlyList<RemoteFileManifestEntry> files,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stagingPath);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(logger);

        string stagingRoot = OwnedDirectoryTree.EnsureRealDirectory(
            stagingPath.OwnerRoot,
            stagingPath.FullPath);
        RemoveUnsafeLinks(stagingPath, logger, cancellationToken);

        HashSet<string> expectedPaths = BuildExpectedInstalledPaths(stagingRoot, files);
        foreach (string filePath in Directory
                     .EnumerateFiles(stagingRoot, "*", FileSystemPathSafety.CreateRecursiveNoLinksOptions())
                     .ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fullPath = LexicalPath.NormalizeFullPath(filePath);
            if (expectedPaths.Contains(fullPath))
            {
                continue;
            }

            File.Delete(fullPath);
            logger.LogInformation(
                "Deleted stale staged package file {FileName}.",
                Path.GetFileName(fullPath));
        }

        foreach (string directoryPath in Directory
                     .EnumerateDirectories(stagingRoot, "*", FileSystemPathSafety.CreateRecursiveNoLinksOptions())
                     .OrderByDescending(path => path.Length)
                     .ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                Directory.Delete(directoryPath);
            }
        }
    }

    private static HashSet<string> BuildExpectedInstalledPaths(
        string stagingRoot,
        IReadOnlyList<RemoteFileManifestEntry> files)
    {
        HashSet<string> expectedPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (RemoteFileManifestEntry file in files)
        {
            string destinationPath = ManifestPathResolver.ResolveInstalledPath(stagingRoot, file.FileName);
            expectedPaths.Add(LexicalPath.NormalizeFullPath(destinationPath));
        }

        return expectedPaths;
    }
}
