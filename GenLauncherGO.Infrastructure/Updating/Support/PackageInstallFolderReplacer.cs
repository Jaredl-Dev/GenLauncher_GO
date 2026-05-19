using System;
using System.IO;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Common;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Updating.Support;

/// <summary>
/// Replaces an installed package folder through a staged move with rollback and restart recovery.
/// </summary>
internal static class PackageInstallFolderReplacer
{
    /// <summary>
    /// Replaces an installed folder using explicit launcher ownership boundaries.
    /// </summary>
    public static void Replace(
        OwnedContentPath temporaryPath,
        OwnedContentPath installedPath,
        OwnedContentPath backupPath,
        ILogger logger)
    {
        Replace(
            temporaryPath,
            installedPath,
            backupPath,
            logger,
            ownedBackupPath => OwnedDirectoryTree.DeleteIfExists(ownedBackupPath));
    }

    /// <summary>
    /// Replaces an installed folder and delegates recovery-backup cleanup through a focused test seam.
    /// </summary>
    internal static void Replace(
        OwnedContentPath temporaryPath,
        OwnedContentPath installedPath,
        OwnedContentPath backupPath,
        ILogger logger,
        Action<OwnedContentPath> deleteBackup)
    {
        ArgumentNullException.ThrowIfNull(temporaryPath);
        ArgumentNullException.ThrowIfNull(installedPath);
        ArgumentNullException.ThrowIfNull(backupPath);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(deleteBackup);

        EnsureRecoveryPathDoesNotOverlapContent(temporaryPath, installedPath, backupPath);

        string temporaryFolderPath = temporaryPath.FullPath;
        string installedFolderPath = installedPath.FullPath;
        EnsureParentDirectoryExists(
            installedPath,
            "Installed package paths must have a parent directory.",
            "Installed package paths must stay inside launcher-owned content.",
            "Installed package paths must be rooted.",
            "The installed package folder cannot contain a reparse point.",
            logger);

        EnsureDirectoryPathHasNoReparsePoints(
            installedPath,
            "Installed package paths must be rooted.",
            "The installed package folder cannot contain a reparse point.",
            logger);
        EnsureDirectoryPathHasNoReparsePoints(
            backupPath,
            "Package backup paths must be rooted.",
            "The package backup folder cannot contain a reparse point.",
            logger);
        EnsureParentDirectoryExists(
            backupPath,
            "Package backup paths must have a parent directory.",
            "Package backup paths must stay inside launcher-owned recovery state.",
            "Package backup paths must be rooted.",
            "The package backup folder cannot contain a reparse point.",
            logger);
        EnsureDirectoryPathHasNoReparsePoints(
            backupPath,
            "Package backup paths must be rooted.",
            "The package backup folder cannot contain a reparse point.",
            logger);
        ReconcilePreviousReplacement(installedPath, backupPath, logger, deleteBackup);
        EnsureDirectoryPathHasNoReparsePoints(
            temporaryPath,
            "Temporary package paths must be rooted.",
            "The temporary package folder cannot contain a reparse point.",
            logger);

        if (!Directory.Exists(temporaryFolderPath))
        {
            throw new DirectoryNotFoundException(
                $"Temporary package folder '{temporaryFolderPath}' was not found.");
        }

        bool backupCreated = false;
        try
        {
            EnsureDirectoryPathHasNoReparsePoints(
                temporaryPath,
                "Temporary package paths must be rooted.",
                "The temporary package folder cannot contain a reparse point.",
                logger);
            EnsureDirectoryPathHasNoReparsePoints(
                installedPath,
                "Installed package paths must be rooted.",
                "The installed package folder cannot contain a reparse point.",
                logger);
            EnsureDirectoryPathHasNoReparsePoints(
                backupPath,
                "Package backup paths must be rooted.",
                "The package backup folder cannot contain a reparse point.",
                logger);
            if (Directory.Exists(installedFolderPath))
            {
                logger.LogInformation(
                    "Moving existing installed package folder {InstalledFolderName} to a staged backup.",
                    Path.GetFileName(installedFolderPath));
                Directory.Move(installedFolderPath, backupPath.FullPath);
                backupCreated = true;
            }

            EnsureDirectoryPathHasNoReparsePoints(
                temporaryPath,
                "Temporary package paths must be rooted.",
                "The temporary package folder cannot contain a reparse point.",
                logger);
            logger.LogInformation(
                "Moving temporary package folder {TemporaryFolderName} into installed package location {InstalledFolderName}.",
                Path.GetFileName(temporaryFolderPath),
                Path.GetFileName(installedFolderPath));
            Directory.Move(temporaryFolderPath, installedFolderPath);
        }
        catch
        {
            RollBackReplacement(
                temporaryFolderPath,
                installedFolderPath,
                backupPath.FullPath,
                backupCreated,
                logger);
            throw;
        }

        if (!backupCreated)
        {
            return;
        }

        try
        {
            deleteBackup(backupPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "Package replacement committed, but obsolete backup cleanup failed for {InstalledFolderName}. The durable recovery backup will be reconciled on the next replacement.",
                Path.GetFileName(installedFolderPath));
        }
    }

    /// <summary>
    /// Resolves the durable backup left by an interrupted or committed replacement before another mutation.
    /// </summary>
    private static void ReconcilePreviousReplacement(
        OwnedContentPath installedPath,
        OwnedContentPath backupPath,
        ILogger logger,
        Action<OwnedContentPath> deleteBackup)
    {
        if (!Directory.Exists(backupPath.FullPath))
        {
            return;
        }

        if (!Directory.Exists(installedPath.FullPath))
        {
            logger.LogWarning(
                "Restoring interrupted package replacement for {InstalledFolderName} from its recovery backup.",
                Path.GetFileName(installedPath.FullPath));
            Directory.Move(backupPath.FullPath, installedPath.FullPath);
            return;
        }

        logger.LogInformation(
            "Removing stale recovery backup for committed package {InstalledFolderName}.",
            Path.GetFileName(installedPath.FullPath));
        deleteBackup(backupPath);
        if (Directory.Exists(backupPath.FullPath))
        {
            throw new IOException(
                $"The stale recovery backup for package '{Path.GetFileName(installedPath.FullPath)}' could not be removed.");
        }
    }

    /// <summary>
    /// Rejects recovery paths that overlap installed content or temporary staging.
    /// </summary>
    private static void EnsureRecoveryPathDoesNotOverlapContent(
        OwnedContentPath temporaryPath,
        OwnedContentPath installedPath,
        OwnedContentPath backupPath)
    {
        if (PathsOverlap(backupPath.FullPath, installedPath.FullPath) ||
            PathsOverlap(backupPath.FullPath, temporaryPath.FullPath))
        {
            throw new ArgumentException(
                "Package recovery backup paths must not overlap installed or temporary package content.",
                nameof(backupPath));
        }
    }

    private static bool PathsOverlap(string firstPath, string secondPath)
    {
        return LexicalPath.IsPathInDirectory(firstPath, secondPath) ||
               LexicalPath.IsPathInDirectory(secondPath, firstPath);
    }

    /// <summary>
    /// Creates an owned parent directory only after verifying its existing path chain is not linked.
    /// </summary>
    private static void EnsureParentDirectoryExists(
        OwnedContentPath directoryPath,
        string missingParentMessage,
        string outsideRootMessage,
        string unrootedPathMessage,
        string linkedPathMessage,
        ILogger logger)
    {
        try
        {
            string parentDirectory = Path.GetDirectoryName(directoryPath.FullPath)
                ?? throw new InvalidDataException(missingParentMessage);
            FileSystemPathSafety.ResolveOwnedSubpath(
                directoryPath.OwnerRoot,
                parentDirectory,
                outsideRootMessage,
                linkedPathMessage);
            Directory.CreateDirectory(parentDirectory);
            FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
                parentDirectory,
                unrootedPathMessage,
                linkedPathMessage);
        }
        catch (InvalidDataException ex)
        {
            logger.LogWarning(
                ex,
                "Blocked package folder replacement because the parent path for {FolderName} failed path-safety validation.",
                Path.GetFileName(directoryPath.FullPath));
            throw new IOException(linkedPathMessage, ex);
        }
    }

    /// <summary>
    /// Verifies that an install or staging directory path does not cross or contain links before replacement.
    /// </summary>
    private static void EnsureDirectoryPathHasNoReparsePoints(
        OwnedContentPath directoryPath,
        string unrootedPathMessage,
        string linkedPathMessage,
        ILogger logger)
    {
        try
        {
            FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
                directoryPath.FullPath,
                unrootedPathMessage,
                linkedPathMessage);
            if (Directory.Exists(directoryPath.FullPath))
            {
                FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(
                    directoryPath.FullPath,
                    linkedPathMessage);
            }
        }
        catch (InvalidDataException ex)
        {
            logger.LogWarning(
                ex,
                "Blocked package folder replacement because {FolderName} contains a reparse point.",
                Path.GetFileName(directoryPath.FullPath));
            throw new IOException(linkedPathMessage, ex);
        }
    }

    /// <summary>
    /// Attempts to restore the prior installed folder after a staged replacement failure.
    /// </summary>
    private static void RollBackReplacement(
        string temporaryPath,
        string installedPath,
        string backupPath,
        bool backupCreated,
        ILogger logger)
    {
        if (!backupCreated || !Directory.Exists(backupPath) || Directory.Exists(installedPath))
        {
            return;
        }

        try
        {
            logger.LogWarning(
                "Rolling back package folder replacement for {InstalledFolderName}.",
                Path.GetFileName(installedPath));
            Directory.Move(backupPath, installedPath);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to roll back package folder replacement for {InstalledFolderName}. Temporary folder exists: {TemporaryFolderExists}",
                Path.GetFileName(installedPath),
                Directory.Exists(temporaryPath));
        }
    }
}
