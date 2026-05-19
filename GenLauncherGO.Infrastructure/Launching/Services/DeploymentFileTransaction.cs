using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Launching.Support;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Launching.Services;

/// <summary>
///     Owns verified backup, staging, deployment, and restoration for individual game-directory files.
/// </summary>
internal sealed class DeploymentFileTransaction
{
    private const int FileBufferSize = 1024 * 128;

    private readonly IHardLinkCreator _hardLinkCreator;
    private readonly ILogger _logger;

    public DeploymentFileTransaction(IHardLinkCreator hardLinkCreator, ILogger logger)
    {
        _hardLinkCreator = hardLinkCreator ?? throw new ArgumentNullException(nameof(hardLinkCreator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Journals intent and completion around committing a verified launcher-owned backup before removing a target.
    /// </summary>
    public DeploymentBackupDocument BackupTargetFile(
        DeploymentStatePaths deploymentPaths,
        FileStream journal,
        string deploymentId,
        string targetRelativePath,
        string targetPath)
    {
        string backupRelativePath = CreateBackupRelativePath(deploymentId, targetRelativePath);
        string backupPath = DeploymentPathResolver.ResolveDeploymentStatePath(
            deploymentPaths.DeploymentDirectory,
            backupRelativePath);
        backupPath = FileSystemPathSafety.ResolveOwnedSubpath(
            deploymentPaths.DeploymentDirectory,
            backupPath,
            "Deployment backup paths",
            "the deployment directory");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath) ?? deploymentPaths.BackupDirectory);
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            Path.GetDirectoryName(backupPath) ?? deploymentPaths.BackupDirectory,
            "Deployment backup paths");
        bool canMoveOriginal = _hardLinkCreator.ArePathsOnSameVolume(targetPath, backupPath);
        string backupStagingPath = canMoveOriginal
            ? string.Empty
            : backupPath + $".partial-{Guid.NewGuid():N}";
        string backupStagingRelativePath = canMoveOriginal
            ? string.Empty
            : DeploymentPathResolver.ToRelativeManifestPath(
                deploymentPaths.DeploymentDirectory,
                backupStagingPath);
        DeploymentStateStore.AppendJournalDurably(
            journal,
            DeploymentJournalRecord.FileBackupStarted(
                targetRelativePath,
                backupRelativePath,
                backupStagingRelativePath));

        DeploymentFileFingerprint backupFingerprint;
        try
        {
            if (canMoveOriginal)
            {
                File.Move(targetPath, backupPath, false);
                backupFingerprint = ComputeFileFingerprint(backupPath);
            }
            else
            {
                backupFingerprint = CopyFileWithMetadataAndFlush(targetPath, backupStagingPath);
                File.Move(backupStagingPath, backupPath, false);
            }
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(backupStagingPath))
            {
                DeleteFileClearingReadOnly(backupStagingPath);
            }

            throw;
        }

        var backedUpRecord = DeploymentJournalRecord.FileBackedUp(
            targetRelativePath,
            backupRelativePath,
            backupFingerprint,
            backupStagingRelativePath);
        if (canMoveOriginal)
        {
            DeploymentStateStore.AppendJournal(journal, backedUpRecord);
        }
        else
        {
            // The completed backup becomes the write-ahead intent for deleting the original on another volume.
            DeploymentStateStore.AppendJournalDurably(journal, backedUpRecord);
        }

        if (!canMoveOriginal)
        {
            EnsureFingerprintMatches(
                targetPath,
                backupFingerprint,
                "The original game file changed while its backup was being committed.");
            DeleteFileClearingReadOnly(targetPath);
        }

        return new DeploymentBackupDocument(
            backupRelativePath,
            backupFingerprint,
            backupStagingRelativePath);
    }

    /// <summary>
    ///     Deploys one file with a hard link first and verified copy fallback.
    /// </summary>
    public (DeploymentMethod Method, DeploymentFileFingerprint Fingerprint, string? FileIdentity) DeployFile(
        FileStream source,
        string sourcePath,
        string targetPath,
        string stagingPath,
        DeploymentFileFingerprint expectedFingerprint)
    {
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            sourcePath,
            "Deployment source paths");

        if (File.Exists(targetPath))
        {
            throw new IOException("A deployment target appeared after its original content was backed up.");
        }

        DeploymentMethod method;
        string? fileIdentity = null;
        try
        {
            bool sourceIsReadOnly = (File.GetAttributes(sourcePath) & FileAttributes.ReadOnly) != 0;
            if (!sourceIsReadOnly &&
                _hardLinkCreator.ArePathsOnSameVolume(sourcePath, stagingPath) &&
                _hardLinkCreator.TryCreateHardLink(stagingPath, sourcePath))
            {
                method = DeploymentMethod.HardLink;
                fileIdentity = DeploymentStateStore.GetFileIdentity(sourcePath);
                if (!string.Equals(
                        fileIdentity,
                        DeploymentStateStore.GetFileIdentity(stagingPath),
                        StringComparison.Ordinal))
                {
                    throw new IOException("The staged hard link did not reference its source file.");
                }
            }
            else
            {
                if (File.Exists(stagingPath))
                {
                    throw new IOException("A deployment staging path was occupied unexpectedly.");
                }

                DeploymentFileFingerprint copiedFingerprint = CopyFileAndFlush(source, stagingPath);
                File.SetLastWriteTimeUtc(stagingPath, File.GetLastWriteTimeUtc(sourcePath));
                if (copiedFingerprint != expectedFingerprint)
                {
                    throw new IOException("The source file changed while it was staged for deployment.");
                }

                method = DeploymentMethod.Copy;
                _logger.LogDebug(
                    "Hard-link deployment was unavailable for {FileName}; used a verified file copy.",
                    Path.GetFileName(targetPath));
            }

            File.Move(stagingPath, targetPath, false);
            return (method, expectedFingerprint, fileIdentity);
        }
        catch
        {
            DeleteOwnedStagingFileIfExpected(stagingPath, expectedFingerprint, false);
            throw;
        }
    }

    /// <summary>
    ///     Replays one persisted file transaction to remove deployed content or restore its original backup.
    /// </summary>
    public void RestoreFile(
        LauncherPaths paths,
        DeploymentStatePaths deploymentPaths,
        DeploymentManifestDocument manifest,
        DeploymentFileDocument file,
        FileStream journal)
    {
        string targetPath = DeploymentPathResolver.ResolveGamePath(paths, file.TargetRelativePath);
        EnsureSafeGameMutationPath(paths, targetPath);
        CleanupGameStagingFile(
            paths,
            file.StagingRelativePath,
            file.DeployedFingerprint,
            false);
        CleanupGameStagingFile(
            paths,
            file.RestoreStagingRelativePath,
            file.BackupFingerprint,
            true);
        CleanupBackupStagingFile(deploymentPaths, file.BackupStagingRelativePath);

        if (string.IsNullOrWhiteSpace(file.BackupRelativePath))
        {
            DeleteDeployedFileWithoutBackup(file, targetPath, journal);
            return;
        }

        string backupPath = DeploymentPathResolver.ResolveDeploymentStatePath(
            deploymentPaths.DeploymentDirectory,
            file.BackupRelativePath);
        backupPath = FileSystemPathSafety.ResolveOwnedSubpath(
            deploymentPaths.DeploymentDirectory,
            backupPath,
            "Deployment backup paths",
            "the deployment directory");
        (DeploymentFileFingerprint Fingerprint, bool Exists) backup =
            ResolveBackupFingerprint(file, backupPath, targetPath);
        if (!backup.Exists)
        {
            return;
        }

        RestoreBackup(
            paths,
            manifest.DeploymentId,
            file,
            targetPath,
            backupPath,
            backup.Fingerprint,
            journal);
    }

    public static DeploymentFileFingerprint ComputeFileFingerprint(FileStream stream)
    {
        stream.Position = 0;
        return new DeploymentFileFingerprint(stream.Length, Convert.ToHexString(SHA256.HashData(stream)));
    }

    public static FileStream OpenDeploymentSource(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileBufferSize,
            FileOptions.SequentialScan);
    }

    /// <summary>
    ///     Verifies that a game-directory mutation target stays in the game folder and does not cross child reparse points.
    /// </summary>
    public static void EnsureSafeGameMutationPath(LauncherPaths paths, string targetPath)
    {
        _ = FileSystemPathSafety.ResolveOwnedSubpath(
            paths.GameDirectory,
            targetPath,
            "Deployment target paths",
            "the game directory");
    }

    private void DeleteDeployedFileWithoutBackup(
        DeploymentFileDocument file,
        string targetPath,
        FileStream journal)
    {
        if (!File.Exists(targetPath))
        {
            return;
        }

        RequireExpectedTarget(
            targetPath,
            file,
            "A deployed game file was modified after launch preparation; cleanup left it untouched.");
        DeleteDeployedTarget(targetPath, file);
        DeploymentStateStore.AppendJournal(
            journal,
            DeploymentJournalRecord.FileCleanupDeleted(file.TargetRelativePath));
    }

    private static (DeploymentFileFingerprint Fingerprint, bool Exists) ResolveBackupFingerprint(
        DeploymentFileDocument file,
        string backupPath,
        string targetPath)
    {
        DeploymentFileFingerprint? backupFingerprint = file.BackupFingerprint;
        bool backupExists = File.Exists(backupPath);
        if (backupExists)
        {
            DeploymentFileFingerprint observedBackupFingerprint = ComputeFileFingerprint(backupPath);
            if (backupFingerprint is not null && observedBackupFingerprint != backupFingerprint)
            {
                throw new InvalidDataException(
                    "A launcher-owned deployment backup changed unexpectedly; the game file was left untouched.");
            }

            backupFingerprint = observedBackupFingerprint;
        }

        if (backupFingerprint is null)
        {
            throw new InvalidDataException(
                "Deployment recovery cannot verify the original game file because its backup fingerprint is missing.");
        }

        if (!backupExists &&
            (!File.Exists(targetPath) || ComputeFileFingerprint(targetPath) != backupFingerprint))
        {
            throw new InvalidDataException(
                "The original game-file backup is missing and the target is not already restored.");
        }

        return (backupFingerprint, backupExists);
    }

    private void RestoreBackup(
        LauncherPaths paths,
        string deploymentId,
        DeploymentFileDocument file,
        string targetPath,
        string backupPath,
        DeploymentFileFingerprint backupFingerprint,
        FileStream journal)
    {
        EnsureSafeGameMutationPath(paths, targetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? paths.GameDirectory);
        EnsureSafeGameMutationPath(paths, targetPath);
        string candidateRestoreStagingPath = CreateSiblingStagingPath(
            targetPath,
            deploymentId,
            "restore");
        bool canMoveOriginal = _hardLinkCreator.ArePathsOnSameVolume(
            backupPath,
            candidateRestoreStagingPath);
        string restoreStagingPath = canMoveOriginal
            ? string.Empty
            : candidateRestoreStagingPath;
        string restoreStagingRelativePath = canMoveOriginal
            ? string.Empty
            : DeploymentPathResolver.ToRelativeManifestPath(
                paths.GameDirectory,
                restoreStagingPath);
        DeploymentStateStore.AppendJournalDurably(
            journal,
            DeploymentJournalRecord.FileCleanupRestoreStarted(
                file.TargetRelativePath,
                file.BackupRelativePath!,
                restoreStagingRelativePath));

        try
        {
            if (File.Exists(targetPath))
            {
                RequireExpectedRestoreTarget(
                    targetPath,
                    file,
                    backupFingerprint,
                    "A game file was modified after launch preparation; its original backup was preserved.");
                DeleteDeployedTarget(targetPath, file);
            }

            if (canMoveOriginal)
            {
                File.Move(backupPath, targetPath, false);
            }
            else
            {
                StageVerifiedFile(
                    backupPath,
                    restoreStagingPath,
                    backupFingerprint);
                File.Move(restoreStagingPath, targetPath, false);
                DeleteFileClearingReadOnly(backupPath);
            }

            DeploymentStateStore.AppendJournal(
                journal,
                DeploymentJournalRecord.FileCleanupRestored(
                    file.TargetRelativePath,
                    file.BackupRelativePath!));
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(restoreStagingPath))
            {
                DeleteOwnedStagingFileIfExpected(
                    restoreStagingPath,
                    backupFingerprint,
                    true);
            }

            throw;
        }
    }

    private static string CreateBackupRelativePath(string deploymentId, string targetRelativePath)
    {
        return LexicalPath.NormalizeRelativePath(Path.Combine(
            DeploymentStateStore.BackupsDirectoryName,
            deploymentId,
            targetRelativePath));
    }

    private static void StageVerifiedFile(
        string sourcePath,
        string stagingPath,
        DeploymentFileFingerprint expectedFingerprint)
    {
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            sourcePath,
            "Deployment source paths");

        if (File.Exists(stagingPath))
        {
            throw new IOException("A deployment staging path was occupied unexpectedly.");
        }

        DeploymentFileFingerprint copiedFingerprint = CopyFileWithMetadataAndFlush(sourcePath, stagingPath);
        if (copiedFingerprint != expectedFingerprint)
        {
            throw new IOException("The source file changed while it was copied into the game directory.");
        }
    }

    private static DeploymentFileFingerprint CopyFileAndFlush(FileStream source, string destinationPath)
    {
        source.Position = 0;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(FileBufferSize);
        try
        {
            using FileStream destination = new(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                FileBufferSize,
                FileOptions.SequentialScan);
            int bytesRead;
            while ((bytesRead = source.Read(buffer, 0, buffer.Length)) != 0)
            {
                destination.Write(buffer, 0, bytesRead);
            }

            destination.Flush(true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return ComputeFileFingerprint(destinationPath);
    }

    /// <summary>
    ///     Uses the Windows file-copy path so alternate streams and security data are retained, then restores
    ///     timestamps and mutable attributes that the platform copy operation does not preserve exactly.
    /// </summary>
    private static DeploymentFileFingerprint CopyFileWithMetadataAndFlush(
        string sourcePath,
        string destinationPath)
    {
        FileAttributes sourceAttributes = File.GetAttributes(sourcePath);
        DateTime creationTimeUtc = File.GetCreationTimeUtc(sourcePath);
        DateTime lastAccessTimeUtc = File.GetLastAccessTimeUtc(sourcePath);
        DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(sourcePath);

        try
        {
            File.Copy(sourcePath, destinationPath, false);
            FileAttributes destinationAttributes = File.GetAttributes(destinationPath);
            if ((destinationAttributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(destinationPath, destinationAttributes & ~FileAttributes.ReadOnly);
            }

            using (FileStream destination = new(
                       destinationPath,
                       FileMode.Open,
                       FileAccess.ReadWrite,
                       FileShare.Read))
            {
                destination.Flush(true);
            }

            DeploymentFileFingerprint fingerprint = ComputeFileFingerprint(destinationPath);

            File.SetCreationTimeUtc(destinationPath, creationTimeUtc);
            File.SetLastAccessTimeUtc(destinationPath, lastAccessTimeUtc);
            File.SetLastWriteTimeUtc(destinationPath, lastWriteTimeUtc);

            const FileAttributes MutableAttributes =
                FileAttributes.ReadOnly |
                FileAttributes.Hidden |
                FileAttributes.System |
                FileAttributes.Archive |
                FileAttributes.Temporary |
                FileAttributes.Offline |
                FileAttributes.NotContentIndexed;
            destinationAttributes = File.GetAttributes(destinationPath);
            File.SetAttributes(
                destinationPath,
                (destinationAttributes & ~MutableAttributes) | (sourceAttributes & MutableAttributes));
            return fingerprint;
        }
        catch
        {
            DeleteFileClearingReadOnly(destinationPath);
            throw;
        }
    }

    private static DeploymentFileFingerprint ComputeFileFingerprint(string path)
    {
        using FileStream stream = OpenDeploymentSource(path);
        return ComputeFileFingerprint(stream);
    }

    private static void EnsureFingerprintMatches(
        string path,
        DeploymentFileFingerprint expectedFingerprint,
        string errorMessage)
    {
        if (ComputeFileFingerprint(path) != expectedFingerprint)
        {
            throw new InvalidDataException(errorMessage);
        }
    }

    private static void RequireExpectedTarget(
        string targetPath,
        DeploymentFileDocument file,
        string conflictMessage)
    {
        if (IsExpectedHardLink(targetPath, file))
        {
            return;
        }

        bool hasRecordedHardLinkIdentity = file.Method == DeploymentMethod.HardLink &&
                                           !string.IsNullOrWhiteSpace(file.DeployedFileIdentity);
        if (hasRecordedHardLinkIdentity ||
            file.DeployedFingerprint is null ||
            ComputeFileFingerprint(targetPath) != file.DeployedFingerprint)
        {
            throw new InvalidDataException(conflictMessage);
        }
    }

    private static void RequireExpectedRestoreTarget(
        string targetPath,
        DeploymentFileDocument file,
        DeploymentFileFingerprint backupFingerprint,
        string conflictMessage)
    {
        if (IsExpectedHardLink(targetPath, file))
        {
            return;
        }

        DeploymentFileFingerprint targetFingerprint = ComputeFileFingerprint(targetPath);
        if (targetFingerprint == backupFingerprint)
        {
            return;
        }

        bool hasRecordedHardLinkIdentity = file.Method == DeploymentMethod.HardLink &&
                                           !string.IsNullOrWhiteSpace(file.DeployedFileIdentity);
        if (hasRecordedHardLinkIdentity ||
            file.DeployedFingerprint is null ||
            targetFingerprint != file.DeployedFingerprint)
        {
            throw new InvalidDataException(conflictMessage);
        }
    }

    private static bool IsExpectedHardLink(string targetPath, DeploymentFileDocument file)
    {
        return file.Method == DeploymentMethod.HardLink &&
               !string.IsNullOrWhiteSpace(file.DeployedFileIdentity) &&
               string.Equals(
                   DeploymentStateStore.GetFileIdentity(targetPath),
                   file.DeployedFileIdentity,
                   StringComparison.Ordinal);
    }

    private static void DeleteFileClearingReadOnly(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }

        File.Delete(path);
    }

    private static void DeleteDeployedTarget(string targetPath, DeploymentFileDocument file)
    {
        if (file.Method == DeploymentMethod.HardLink)
        {
            File.Delete(targetPath);
            return;
        }

        DeleteFileClearingReadOnly(targetPath);
    }

    private void CleanupGameStagingFile(
        LauncherPaths paths,
        string? stagingRelativePath,
        DeploymentFileFingerprint? expectedFingerprint,
        bool clearReadOnly)
    {
        if (string.IsNullOrWhiteSpace(stagingRelativePath))
        {
            return;
        }

        string stagingPath = DeploymentPathResolver.ResolveGamePath(paths, stagingRelativePath);
        EnsureSafeGameMutationPath(paths, stagingPath);
        if (!File.Exists(stagingPath))
        {
            return;
        }

        if (expectedFingerprint is not null &&
            ComputeFileFingerprint(stagingPath) != expectedFingerprint)
        {
            _logger.LogWarning(
                "Removed incomplete transaction staging file {FileName} during deployment recovery.",
                Path.GetFileName(stagingPath));
        }

        if (clearReadOnly)
        {
            DeleteFileClearingReadOnly(stagingPath);
        }
        else
        {
            File.Delete(stagingPath);
        }
    }

    private static void CleanupBackupStagingFile(
        DeploymentStatePaths deploymentPaths,
        string? stagingRelativePath)
    {
        if (string.IsNullOrWhiteSpace(stagingRelativePath))
        {
            return;
        }

        string stagingPath = DeploymentPathResolver.ResolveDeploymentStatePath(
            deploymentPaths.DeploymentDirectory,
            stagingRelativePath);
        stagingPath = FileSystemPathSafety.ResolveOwnedSubpath(
            deploymentPaths.DeploymentDirectory,
            stagingPath,
            "Deployment backup staging paths",
            "the deployment directory");
        if (File.Exists(stagingPath))
        {
            DeleteFileClearingReadOnly(stagingPath);
        }
    }

    private void DeleteOwnedStagingFileIfExpected(
        string stagingPath,
        DeploymentFileFingerprint expectedFingerprint,
        bool clearReadOnly)
    {
        if (!File.Exists(stagingPath))
        {
            return;
        }

        try
        {
            if (ComputeFileFingerprint(stagingPath) == expectedFingerprint)
            {
                if (clearReadOnly)
                {
                    DeleteFileClearingReadOnly(stagingPath);
                }
                else
                {
                    File.Delete(stagingPath);
                }

                return;
            }

            _logger.LogWarning(
                "Left deployment staging file {FileName} untouched because its contents changed unexpectedly.",
                Path.GetFileName(stagingPath));
        }
        catch (IOException exception)
        {
            _logger.LogWarning(
                exception,
                "Could not inspect deployment staging file {FileName}.",
                Path.GetFileName(stagingPath));
        }
    }

    public static string CreateSiblingStagingPath(string targetPath, string deploymentId, string operation)
    {
        string directory = Path.GetDirectoryName(targetPath)
                           ?? throw new InvalidOperationException(
                               "Deployment target paths must have a parent directory.");
        string fileName = Path.GetFileName(targetPath);
        return Path.Combine(
            directory,
            $".{fileName}.GenLauncherGO-{operation}-{deploymentId}-{Guid.NewGuid():N}.tmp");
    }
}
