using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Launching.Support;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Launching.Services;

/// <summary>
/// Deploys selected package files into the game directory and persists enough manifest state to undo the deployment.
/// </summary>
internal sealed class FileSystemDeploymentService
{
    private const int FileBufferSize = 1024 * 128;

    private readonly IHardLinkCreator _hardLinkCreator;

    private readonly ILogger<FileSystemDeploymentService> _logger;

    private readonly DeploymentStateStore _stateStore;

    public FileSystemDeploymentService(
        IHardLinkCreator hardLinkCreator,
        ILogger<FileSystemDeploymentService> logger)
    {
        _hardLinkCreator = hardLinkCreator ?? throw new ArgumentNullException(nameof(hardLinkCreator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stateStore = new DeploymentStateStore(_logger);
    }

    /// <summary>
    /// Prepares the game directory by deploying the selected packages.
    /// </summary>
    public DeploymentResult Prepare(
        LauncherPaths paths,
        IReadOnlyList<DeploymentPackage> packages,
        IReadOnlyList<string> disabledTargetRelativePaths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(disabledTargetRelativePaths);

        IReadOnlyList<string> normalizedDisabledTargetRelativePaths = disabledTargetRelativePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => LexicalPath.NormalizeRelativePath(path.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        try
        {
            using FileStream deploymentLock = DeploymentStateStore.AcquireDeploymentLock(paths);
            return PrepareWithLock(
                paths,
                packages,
                normalizedDisabledTargetRelativePaths,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Deployment preparation failed before deployment recovery could run.");
            return DeploymentResult.Failure(
                new[]
                {
                    new DeploymentFailure(
                        DeploymentFailureKind.FileSystem,
                        paths.GameDirectory,
                        ex.Message),
                });
        }
    }

    /// <summary>
    /// Cleans the active deployment from the game directory.
    /// </summary>
    public DeploymentResult Cleanup(LauncherPaths paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);

        try
        {
            using FileStream deploymentLock = DeploymentStateStore.AcquireDeploymentLock(paths);
            return CleanupCore(paths, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Deployment cleanup failed.");
            return DeploymentResult.Failure(
                new[]
                {
                    new DeploymentFailure(
                        DeploymentFailureKind.FileSystem,
                        paths.GameDirectory,
                        ex.Message),
                });
        }
    }

    /// <summary>
    /// Recovers interrupted deployment work from the persisted manifest or journal.
    /// </summary>
    public DeploymentResult Recover(LauncherPaths paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);

        try
        {
            using FileStream deploymentLock = DeploymentStateStore.AcquireDeploymentLock(paths);
            return RecoverCore(paths, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Deployment recovery failed.");
            return DeploymentResult.Failure(
                new[]
                {
                    new DeploymentFailure(
                        DeploymentFailureKind.Manifest,
                        paths.GameDirectory,
                        ex.Message),
                });
        }
    }

    /// <summary>
    /// Prepares a deployment while the deployment operation lock is held.
    /// </summary>
    private DeploymentResult PrepareWithLock(
        LauncherPaths launcherPaths,
        IReadOnlyList<DeploymentPackage> packages,
        IReadOnlyList<string> disabledTargetRelativePaths,
        CancellationToken cancellationToken)
    {
        try
        {
            DeploymentResult cleanupResult = CleanupCore(launcherPaths, cancellationToken);
            if (!cleanupResult.Succeeded)
            {
                return cleanupResult;
            }

            string deploymentId = Guid.NewGuid().ToString("N");
            DeploymentStatePaths paths = DeploymentStateStore.CreatePaths(launcherPaths, deploymentId);
            OwnedDirectoryTree.EnsureExists(launcherPaths.OwnedGameDataDirectory, paths.DeploymentDirectory);
            OwnedDirectoryTree.EnsureExists(paths.DeploymentDirectory, paths.BackupDirectory);
            if (File.Exists(paths.JournalPath))
            {
                File.Delete(paths.JournalPath);
            }

            string gameRoot = PhysicalDirectoryPath.ResolveExisting(launcherPaths.GameDirectory);
            string gameRootIdentity = DeploymentStateStore.GetGameRootIdentity(gameRoot);
            DeploymentStateStore.AppendJournal(
                paths.JournalPath,
                DeploymentJournalRecord.DeploymentStarted(
                    deploymentId,
                    gameRoot,
                    gameRootIdentity,
                    launcherPaths.Game));

            IReadOnlyList<ResolvedDeploymentFile> files =
                DeploymentFilePlanner.ResolveDeploymentFiles(packages);
            var createdDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<DeploymentFileDocument>();
            var backedUpTargetPaths =
                new Dictionary<string, DeploymentBackupDocument>(StringComparer.OrdinalIgnoreCase);
            var deployedTargetPaths = files
                .Select(file => file.TargetRelativePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            BackupDisabledTargets(
                launcherPaths,
                disabledTargetRelativePaths,
                paths,
                deploymentId,
                deployedTargetPaths,
                backedUpTargetPaths,
                entries,
                cancellationToken);

            foreach (ResolvedDeploymentFile file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string targetPath = DeploymentPathResolver.ResolveGamePath(launcherPaths, file.TargetRelativePath);
                EnsureSafeGameMutationPath(launcherPaths, targetPath);
                string targetDirectory = Path.GetDirectoryName(targetPath) ?? launcherPaths.GameDirectory;
                foreach (string directory in DeploymentFilePlanner.GetDirectoriesToCreate(
                             launcherPaths.GameDirectory,
                             targetDirectory))
                {
                    if (!Directory.Exists(directory))
                    {
                        EnsureSafeGameMutationPath(launcherPaths, directory);
                        string relativeDirectory = DeploymentPathResolver.ToRelativeManifestPath(
                            launcherPaths.GameDirectory,
                            directory);
                        DeploymentStateStore.AppendJournal(
                            paths.JournalPath,
                            DeploymentJournalRecord.DirectoryCreated(relativeDirectory));
                        Directory.CreateDirectory(directory);
                        EnsureSafeGameMutationPath(launcherPaths, directory);
                        createdDirectories.Add(relativeDirectory);
                    }
                }

                EnsureSafeGameMutationPath(launcherPaths, targetPath);
                DeploymentBackupDocument? backup;
                if (!backedUpTargetPaths.TryGetValue(file.TargetRelativePath, out backup) &&
                    File.Exists(targetPath))
                {
                    backup = BackupTargetFile(
                        paths,
                        deploymentId,
                        file.TargetRelativePath,
                        targetPath);
                    backedUpTargetPaths[file.TargetRelativePath] = backup;
                }

                DeploymentFileFingerprint sourceFingerprint = ComputeFileFingerprint(file.SourcePath);
                string stagingPath = CreateSiblingStagingPath(targetPath, deploymentId, "deploy");
                string stagingRelativePath = DeploymentPathResolver.ToRelativeManifestPath(
                    launcherPaths.GameDirectory,
                    stagingPath);
                DeploymentStateStore.AppendJournal(paths.JournalPath, DeploymentJournalRecord.FileDeploymentStarted(
                    file.TargetRelativePath,
                    backup?.RelativePath,
                    sourceFingerprint,
                    backup?.Fingerprint,
                    stagingRelativePath));

                EnsureSafeGameMutationPath(launcherPaths, targetPath);
                (DeploymentMethod Method, DeploymentFileFingerprint Fingerprint) deployment = DeployFile(
                    file.SourcePath,
                    targetPath,
                    stagingPath,
                    sourceFingerprint);
                DeploymentStateStore.AppendJournal(paths.JournalPath, DeploymentJournalRecord.FileDeployed(
                    file.TargetRelativePath,
                    deployment.Method,
                    backup?.RelativePath,
                    deployment.Fingerprint,
                    backup?.Fingerprint,
                    stagingRelativePath));
                cancellationToken.ThrowIfCancellationRequested();

                entries.Add(new DeploymentFileDocument(
                    file.TargetRelativePath,
                    deployment.Method,
                    backup?.RelativePath,
                    deployment.Fingerprint,
                    backup?.Fingerprint,
                    stagingRelativePath,
                    backup?.StagingRelativePath));
            }

            DeploymentManifestDocument document = new(
                SchemaVersion: DeploymentStateStore.CurrentSchemaVersion,
                deploymentId,
                entries,
                createdDirectories.OrderByDescending(path => path.Length).ToList(),
                gameRoot,
                gameRootIdentity,
                launcherPaths.Game);
            cancellationToken.ThrowIfCancellationRequested();
            DeploymentStateStore.WriteManifest(paths.ActiveManifestPath, document);
            _logger.LogInformation("Prepared deployment {DeploymentId} with {FileCount} file(s).", deploymentId,
                entries.Count);
            return DeploymentResult.Success();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Deployment preparation was canceled; recovering any partial game-folder mutation.");
            RecoverCore(launcherPaths, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deployment preparation failed.");
            DeploymentFailure prepareFailure = new(
                DeploymentFailureKind.FileSystem,
                launcherPaths.GameDirectory,
                ex.Message);

            DeploymentResult recoveryResult;
            try
            {
                recoveryResult = RecoverCore(launcherPaths, CancellationToken.None);
            }
            catch (Exception recoveryException)
            {
                _logger.LogError(recoveryException, "Deployment recovery failed after preparation failure.");
                recoveryResult = DeploymentResult.Failure(
                    new[]
                    {
                        new DeploymentFailure(
                            DeploymentFailureKind.Manifest,
                            launcherPaths.GameDirectory,
                            recoveryException.Message),
                    });
            }

            if (recoveryResult.Succeeded)
            {
                return DeploymentResult.Failure(new[] { prepareFailure });
            }

            return DeploymentResult.Failure(
                new[] { prepareFailure }.Concat(recoveryResult.Failures).ToArray());
        }
    }

    /// <summary>
    /// Cleans a deployment while the deployment operation lock is held.
    /// </summary>
    private DeploymentResult CleanupCore(LauncherPaths paths, CancellationToken cancellationToken)
    {
        DeploymentManifestDocument? manifest = RestoreActiveDeployment(paths, cancellationToken);
        if (manifest is not null)
        {
            _logger.LogInformation("Cleaned deployment {DeploymentId}.", manifest.DeploymentId);
        }

        return DeploymentResult.Success();
    }

    /// <summary>
    /// Recovers deployment state while the deployment operation lock is held.
    /// </summary>
    private DeploymentResult RecoverCore(LauncherPaths paths, CancellationToken cancellationToken)
    {
        DeploymentManifestDocument? manifest = RestoreActiveDeployment(paths, cancellationToken);
        if (manifest is not null)
        {
            _logger.LogInformation("Recovered deployment state for {DeploymentId}.", manifest.DeploymentId);
        }

        return DeploymentResult.Success();
    }

    /// <summary>
    /// Restores game files and removes the durable state for one active or interrupted deployment.
    /// </summary>
    private DeploymentManifestDocument? RestoreActiveDeployment(
        LauncherPaths paths,
        CancellationToken cancellationToken)
    {
        DeploymentStatePaths deploymentPaths = DeploymentStateStore.CreatePaths(paths, deploymentId: string.Empty);
        DeploymentManifestDocument? manifest = _stateStore.ReadManifestOrJournal(paths, deploymentPaths);

        if (manifest is null)
        {
            DeploymentStateStore.DeleteDeploymentStateFiles(deploymentPaths);
            return null;
        }

        CleanupManifest(paths, deploymentPaths, manifest, cancellationToken);
        DeleteEmptyBackupDirectories(deploymentPaths, cancellationToken);
        DeploymentStateStore.DeleteDeploymentStateFiles(deploymentPaths);
        return manifest;
    }

    /// <summary>
    /// Deploys a file with a hard link first and copy fallback.
    /// </summary>
    private (DeploymentMethod Method, DeploymentFileFingerprint Fingerprint) DeployFile(
        string sourcePath,
        string targetPath,
        string stagingPath,
        DeploymentFileFingerprint expectedFingerprint)
    {
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            sourcePath,
            "Deployment source paths must be rooted.",
            "Deployment source paths must not contain reparse points.");

        if (File.Exists(targetPath))
        {
            throw new IOException("A deployment target appeared after its original content was backed up.");
        }

        DeploymentMethod method;
        try
        {
            bool sourceIsReadOnly = (File.GetAttributes(sourcePath) & FileAttributes.ReadOnly) != 0;
            if (!sourceIsReadOnly &&
                ArePathsOnSameVolume(sourcePath, stagingPath) &&
                _hardLinkCreator.TryCreateHardLink(stagingPath, sourcePath))
            {
                method = DeploymentMethod.HardLink;
                EnsureFingerprintMatches(stagingPath, expectedFingerprint, "The staged hard link did not match its source.");
            }
            else
            {
                if (File.Exists(stagingPath))
                {
                    throw new IOException("A deployment staging path was occupied unexpectedly.");
                }

                DeploymentFileFingerprint copiedFingerprint = CopyFileAndFlush(sourcePath, stagingPath);
                File.SetLastWriteTimeUtc(stagingPath, File.GetLastWriteTimeUtc(sourcePath));
                if (copiedFingerprint != expectedFingerprint)
                {
                    throw new IOException("The source file changed while it was staged for deployment.");
                }

                EnsureFingerprintMatches(stagingPath, expectedFingerprint, "The staged copy did not match its source.");
                method = DeploymentMethod.Copy;
                _logger.LogInformation(
                    "Hard-link deployment was unavailable for {FileName}; used a verified file copy.",
                    Path.GetFileName(targetPath));
            }

            File.Move(stagingPath, targetPath, overwrite: false);
            EnsureFingerprintMatches(
                targetPath,
                expectedFingerprint,
                "The deployed target did not match its staged content.");
            return (method, expectedFingerprint);
        }
        catch
        {
            DeleteOwnedStagingFileIfExpected(stagingPath, expectedFingerprint, clearReadOnly: false);
            throw;
        }
    }

    /// <summary>
    /// Backs up deployment targets that should be hidden without deploying a replacement file.
    /// </summary>
    private void BackupDisabledTargets(
        LauncherPaths launcherPaths,
        IReadOnlyList<string> disabledTargetRelativePaths,
        DeploymentStatePaths deploymentPaths,
        string deploymentId,
        IReadOnlySet<string> deployedTargetPaths,
        Dictionary<string, DeploymentBackupDocument> backedUpTargetPaths,
        List<DeploymentFileDocument> entries,
        CancellationToken cancellationToken)
    {
        foreach (string disabledTargetRelativePath in disabledTargetRelativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string targetRelativePath = DeploymentPathResolver.NormalizeManifestPath(disabledTargetRelativePath);
            if (backedUpTargetPaths.ContainsKey(targetRelativePath))
            {
                continue;
            }

            string targetPath = DeploymentPathResolver.ResolveGamePath(launcherPaths, targetRelativePath);
            EnsureSafeGameMutationPath(launcherPaths, targetPath);
            if (!File.Exists(targetPath))
            {
                _logger.LogDebug(
                    "Skipped disabling base game file {FileName} because it does not exist.",
                    Path.GetFileName(targetPath));
                continue;
            }

            DeploymentBackupDocument backup = BackupTargetFile(
                deploymentPaths,
                deploymentId,
                targetRelativePath,
                targetPath);
            backedUpTargetPaths[targetRelativePath] = backup;

            if (!deployedTargetPaths.Contains(targetRelativePath))
            {
                entries.Add(new DeploymentFileDocument(
                    targetRelativePath,
                    DeploymentMethod.Copy,
                    backup.RelativePath,
                    DeployedFingerprint: null,
                    BackupFingerprint: backup.Fingerprint,
                    StagingRelativePath: null,
                    BackupStagingRelativePath: backup.StagingRelativePath));
            }

            _logger.LogInformation(
                "Temporarily disabled base game file {FileName} for modded launch deployment.",
                Path.GetFileName(targetPath));
        }
    }

    /// <summary>
    /// Journals intent and completion around committing a verified launcher-owned backup before removing a target.
    /// </summary>
    private static DeploymentBackupDocument BackupTargetFile(
        DeploymentStatePaths deploymentPaths,
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
            "Deployment backup paths must stay inside the deployment directory.",
            "Deployment backup paths must not contain reparse points.");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath) ?? deploymentPaths.BackupDirectory);
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            Path.GetDirectoryName(backupPath) ?? deploymentPaths.BackupDirectory,
            "Deployment backup paths must be rooted.",
            "Deployment backup paths must not contain reparse points.");
        bool canMoveOriginal = ArePathsOnSameVolume(targetPath, backupPath);
        string backupStagingPath = canMoveOriginal
            ? string.Empty
            : backupPath + $".partial-{Guid.NewGuid():N}";
        string backupStagingRelativePath = canMoveOriginal
            ? string.Empty
            : DeploymentPathResolver.ToRelativeManifestPath(
                deploymentPaths.DeploymentDirectory,
                backupStagingPath);
        DeploymentStateStore.AppendJournal(
            deploymentPaths.JournalPath,
            DeploymentJournalRecord.FileBackupStarted(
                targetRelativePath,
                backupRelativePath,
                backupStagingRelativePath));

        DeploymentFileFingerprint backupFingerprint;
        try
        {
            if (canMoveOriginal)
            {
                File.Move(targetPath, backupPath, overwrite: false);
                backupFingerprint = ComputeFileFingerprint(backupPath);
            }
            else
            {
                backupFingerprint = CopyFileWithMetadataAndFlush(targetPath, backupStagingPath);
                EnsureFingerprintMatches(
                    backupStagingPath,
                    backupFingerprint,
                    "The launcher-owned backup did not match the original game file.");
                File.Move(backupStagingPath, backupPath, overwrite: false);
            }

            EnsureFingerprintMatches(
                backupPath,
                backupFingerprint,
                "The launcher-owned backup did not match the original game file.");
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(backupStagingPath))
            {
                DeleteFileClearingReadOnly(backupStagingPath);
            }

            throw;
        }

        DeploymentStateStore.AppendJournal(
            deploymentPaths.JournalPath,
            DeploymentJournalRecord.FileBackedUp(
                targetRelativePath,
                backupRelativePath,
                backupFingerprint,
                backupStagingRelativePath));

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

    private static string CreateBackupRelativePath(string deploymentId, string targetRelativePath)
    {
        return LexicalPath.NormalizeRelativePath(Path.Combine(
            DeploymentStateStore.BackupsDirectoryName,
            deploymentId,
            targetRelativePath));
    }

    /// <summary>
    /// Replays persisted deployment state to remove deployed files and restore original backups.
    /// </summary>
    private void CleanupManifest(
        LauncherPaths paths,
        DeploymentStatePaths deploymentPaths,
        DeploymentManifestDocument manifest,
        CancellationToken cancellationToken)
    {
        foreach (DeploymentFileDocument file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string targetPath = DeploymentPathResolver.ResolveGamePath(paths, file.TargetRelativePath);
            EnsureSafeGameMutationPath(paths, targetPath);
            CleanupGameStagingFile(
                paths,
                file.StagingRelativePath,
                file.DeployedFingerprint,
                clearReadOnly: false);
            CleanupGameStagingFile(
                paths,
                file.RestoreStagingRelativePath,
                file.BackupFingerprint,
                clearReadOnly: true);
            CleanupBackupStagingFile(deploymentPaths, file.BackupStagingRelativePath);

            if (string.IsNullOrWhiteSpace(file.BackupRelativePath))
            {
                if (File.Exists(targetPath))
                {
                    RequireExpectedTargetFingerprint(
                        targetPath,
                        file.DeployedFingerprint,
                        "A deployed game file was modified after launch preparation; cleanup left it untouched.");
                    DeleteDeployedTarget(targetPath, file);
                    DeploymentStateStore.AppendJournal(
                        deploymentPaths.JournalPath,
                        DeploymentJournalRecord.FileCleanupDeleted(file.TargetRelativePath));
                }

                continue;
            }

            string backupPath = DeploymentPathResolver.ResolveDeploymentStatePath(
                deploymentPaths.DeploymentDirectory,
                file.BackupRelativePath);
            backupPath = FileSystemPathSafety.ResolveOwnedSubpath(
                deploymentPaths.DeploymentDirectory,
                backupPath,
                "Deployment backup paths must stay inside the deployment directory.",
                "Deployment backup paths must not contain reparse points.");
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

            if (File.Exists(targetPath))
            {
                DeploymentFileFingerprint targetFingerprint = ComputeFileFingerprint(targetPath);
                if (!backupExists && targetFingerprint == backupFingerprint)
                {
                    continue;
                }

                if (targetFingerprint != backupFingerprint &&
                    (file.DeployedFingerprint is null || targetFingerprint != file.DeployedFingerprint))
                {
                    throw new InvalidDataException(
                        "A game file was modified after launch preparation; its original backup was preserved.");
                }
            }

            if (!backupExists)
            {
                throw new InvalidDataException(
                    "The original game-file backup is missing and the target is not already restored.");
            }

            EnsureSafeGameMutationPath(paths, targetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? paths.GameDirectory);
            EnsureSafeGameMutationPath(paths, targetPath);
            bool canMoveOriginal = ArePathsOnSameVolume(backupPath, targetPath);
            string restoreStagingPath = canMoveOriginal
                ? string.Empty
                : CreateSiblingStagingPath(targetPath, manifest.DeploymentId, "restore");
            string restoreStagingRelativePath = canMoveOriginal
                ? string.Empty
                : DeploymentPathResolver.ToRelativeManifestPath(
                    paths.GameDirectory,
                    restoreStagingPath);
            DeploymentStateStore.AppendJournal(
                deploymentPaths.JournalPath,
                DeploymentJournalRecord.FileCleanupRestoreStarted(
                    file.TargetRelativePath,
                    file.BackupRelativePath,
                    restoreStagingRelativePath));

            try
            {
                if (File.Exists(targetPath))
                {
                    RequireExpectedRestoreTargetFingerprint(
                        targetPath,
                        file.DeployedFingerprint,
                        backupFingerprint,
                        "A deployed game file changed while its original was being restored.");
                    DeleteDeployedTarget(targetPath, file);
                }

                if (canMoveOriginal)
                {
                    File.Move(backupPath, targetPath, overwrite: false);
                }
                else
                {
                    StageVerifiedFile(backupPath, restoreStagingPath, backupFingerprint);
                    File.Move(restoreStagingPath, targetPath, overwrite: false);
                    DeleteFileClearingReadOnly(backupPath);
                }

                EnsureFingerprintMatches(
                    targetPath,
                    backupFingerprint,
                    "The restored game file did not match its launcher-owned backup.");
                DeploymentStateStore.AppendJournal(
                    deploymentPaths.JournalPath,
                    DeploymentJournalRecord.FileCleanupRestored(
                        file.TargetRelativePath,
                        file.BackupRelativePath));
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(restoreStagingPath))
                {
                    DeleteOwnedStagingFileIfExpected(
                        restoreStagingPath,
                        backupFingerprint,
                        clearReadOnly: true);
                }

                throw;
            }
        }

        foreach (string relativeDirectory in manifest.CreatedDirectories.OrderByDescending(path => path.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string directoryPath = DeploymentPathResolver.ResolveGamePath(paths, relativeDirectory);
            EnsureSafeGameMutationPath(paths, directoryPath);
            if (!Directory.Exists(directoryPath))
            {
                continue;
            }

            if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                Directory.Delete(directoryPath);
                continue;
            }

            _logger.LogInformation(
                "Left deployment-created directory {DirectoryName} because it contains non-deployed files.",
                Path.GetFileName(directoryPath));
        }
    }

    private void StageVerifiedFile(
        string sourcePath,
        string stagingPath,
        DeploymentFileFingerprint expectedFingerprint)
    {
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            sourcePath,
            "Deployment source paths must be rooted.",
            "Deployment source paths must not contain reparse points.");

        if (ArePathsOnSameVolume(sourcePath, stagingPath) &&
            _hardLinkCreator.TryCreateHardLink(stagingPath, sourcePath))
        {
            EnsureFingerprintMatches(
                stagingPath,
                expectedFingerprint,
                "The staged hard link did not match its source.");
            return;
        }

        if (File.Exists(stagingPath))
        {
            throw new IOException("A deployment staging path was occupied unexpectedly.");
        }

        DeploymentFileFingerprint copiedFingerprint = CopyFileWithMetadataAndFlush(sourcePath, stagingPath);
        if (copiedFingerprint != expectedFingerprint)
        {
            throw new IOException("The source file changed while it was copied into the game directory.");
        }

        EnsureFingerprintMatches(
            stagingPath,
            expectedFingerprint,
            "The staged file copy did not match its source.");
    }

    private static DeploymentFileFingerprint CopyFileAndFlush(string sourcePath, string destinationPath)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(FileBufferSize);
        try
        {
            using FileStream source = new(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileBufferSize,
                FileOptions.SequentialScan);
            using FileStream destination = new(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                FileBufferSize,
                FileOptions.SequentialScan | FileOptions.WriteThrough);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            long length = 0;
            int bytesRead;
            while ((bytesRead = source.Read(buffer, 0, buffer.Length)) != 0)
            {
                destination.Write(buffer, 0, bytesRead);
                hash.AppendData(buffer, 0, bytesRead);
                length += bytesRead;
            }

            destination.Flush(flushToDisk: true);
            return new DeploymentFileFingerprint(length, Convert.ToHexString(hash.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Uses the Windows file-copy path so alternate streams and security data are retained, then restores
    /// timestamps and mutable attributes that the platform copy operation does not preserve exactly.
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
            File.Copy(sourcePath, destinationPath, overwrite: false);
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
                destination.Flush(flushToDisk: true);
            }

            DeploymentFileFingerprint fingerprint = ComputeFileFingerprint(destinationPath);

            File.SetCreationTimeUtc(destinationPath, creationTimeUtc);
            File.SetLastAccessTimeUtc(destinationPath, lastAccessTimeUtc);
            File.SetLastWriteTimeUtc(destinationPath, lastWriteTimeUtc);

            const FileAttributes mutableAttributes =
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
                (destinationAttributes & ~mutableAttributes) | (sourceAttributes & mutableAttributes));
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
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileBufferSize,
            FileOptions.SequentialScan);
        return new DeploymentFileFingerprint(stream.Length, Convert.ToHexString(SHA256.HashData(stream)));
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

    private static void RequireExpectedTargetFingerprint(
        string targetPath,
        DeploymentFileFingerprint? expectedFingerprint,
        string conflictMessage)
    {
        if (expectedFingerprint is null || ComputeFileFingerprint(targetPath) != expectedFingerprint)
        {
            throw new InvalidDataException(conflictMessage);
        }
    }

    private static void RequireExpectedRestoreTargetFingerprint(
        string targetPath,
        DeploymentFileFingerprint? deployedFingerprint,
        DeploymentFileFingerprint backupFingerprint,
        string conflictMessage)
    {
        DeploymentFileFingerprint targetFingerprint = ComputeFileFingerprint(targetPath);
        if (targetFingerprint != backupFingerprint &&
            (deployedFingerprint is null || targetFingerprint != deployedFingerprint))
        {
            throw new InvalidDataException(conflictMessage);
        }
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
            "Deployment backup staging paths must stay inside the deployment directory.",
            "Deployment backup staging paths must not contain reparse points.");
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
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not inspect deployment staging file {FileName}.", Path.GetFileName(stagingPath));
        }
    }

    private static string CreateSiblingStagingPath(string targetPath, string deploymentId, string operation)
    {
        string directory = Path.GetDirectoryName(targetPath)
                           ?? throw new InvalidOperationException("Deployment target paths must have a parent directory.");
        string fileName = Path.GetFileName(targetPath);
        return Path.Combine(
            directory,
            $".{fileName}.GenLauncherGO-{operation}-{deploymentId}-{Guid.NewGuid():N}.tmp");
    }

    private static bool ArePathsOnSameVolume(string firstPath, string secondPath)
    {
        string firstDirectory = Directory.Exists(firstPath)
            ? firstPath
            : Path.GetDirectoryName(firstPath)
              ?? throw new InvalidOperationException("Deployment file paths must have a parent directory.");
        string secondDirectory = Directory.Exists(secondPath)
            ? secondPath
            : Path.GetDirectoryName(secondPath)
              ?? throw new InvalidOperationException("Deployment file paths must have a parent directory.");
        return PhysicalDirectoryPath.GetIdentity(firstDirectory).VolumeSerialNumber ==
               PhysicalDirectoryPath.GetIdentity(secondDirectory).VolumeSerialNumber;
    }

    /// <summary>
    /// Verifies that a game-directory mutation target stays in the game folder and does not cross child reparse points.
    /// </summary>
    private static void EnsureSafeGameMutationPath(LauncherPaths paths, string targetPath)
    {
        _ = FileSystemPathSafety.ResolveOwnedSubpath(
            paths.GameDirectory,
            targetPath,
            "Deployment target paths must stay inside the game directory.",
            "Deployment target paths must not contain reparse points.");
    }

    private static void DeleteEmptyBackupDirectories(
        DeploymentStatePaths deploymentPaths,
        CancellationToken cancellationToken)
    {
        string backupRoot = Path.Combine(
            deploymentPaths.DeploymentDirectory,
            DeploymentStateStore.BackupsDirectoryName);
        if (!Directory.Exists(backupRoot))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        OwnedDirectoryTree.DeleteEmptyDirectories(
            new GenLauncherGO.Core.Mods.Models.OwnedContentPath(
                deploymentPaths.DeploymentDirectory,
                backupRoot));
    }

}
