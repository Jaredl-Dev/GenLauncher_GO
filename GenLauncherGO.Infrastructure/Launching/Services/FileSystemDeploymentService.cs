using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Launching.Support;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Launching.Services;

/// <summary>
///     Deploys selected package files into the game directory and persists enough manifest state to undo the deployment.
/// </summary>
internal sealed class FileSystemDeploymentService
{
    private readonly DeploymentFileTransaction _fileTransaction;

    private readonly ILogger<FileSystemDeploymentService> _logger;

    private readonly DeploymentStateStore _stateStore;

    public FileSystemDeploymentService(
        IHardLinkCreator hardLinkCreator,
        ILogger<FileSystemDeploymentService> logger)
    {
        ArgumentNullException.ThrowIfNull(hardLinkCreator);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fileTransaction = new DeploymentFileTransaction(hardLinkCreator, logger);
        _stateStore = new DeploymentStateStore(_logger);
    }

    /// <summary>
    ///     Prepares the game directory by deploying the selected packages.
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

        return ExecuteLocked(
            paths,
            () => PrepareWithLock(
                paths,
                packages,
                normalizedDisabledTargetRelativePaths,
                cancellationToken),
            DeploymentFailureKind.FileSystem,
            exception => _logger.LogError(
                exception,
                "Deployment preparation failed before deployment recovery could run."));
    }

    /// <summary>
    ///     Cleans the active deployment from the game directory.
    /// </summary>
    public DeploymentResult Cleanup(LauncherPaths paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return ExecuteLocked(
            paths,
            () => RestoreCore(paths, RestoreOperationKind.Cleanup, cancellationToken),
            DeploymentFailureKind.FileSystem,
            exception => _logger.LogError(exception, "Deployment cleanup failed."));
    }

    /// <summary>
    ///     Recovers interrupted deployment work from the persisted manifest or journal.
    /// </summary>
    public DeploymentResult Recover(LauncherPaths paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return ExecuteLocked(
            paths,
            () => RestoreCore(paths, RestoreOperationKind.Recovery, cancellationToken),
            DeploymentFailureKind.Manifest,
            exception => _logger.LogError(exception, "Deployment recovery failed."));
    }

    /// <summary>
    ///     Prepares a deployment while the deployment operation lock is held.
    /// </summary>
    private DeploymentResult PrepareWithLock(
        LauncherPaths launcherPaths,
        IReadOnlyList<DeploymentPackage> packages,
        IReadOnlyList<string> disabledTargetRelativePaths,
        CancellationToken cancellationToken)
    {
        List<FileStream> sourceStreams = [];
        long preparationStartedTimestamp = Stopwatch.GetTimestamp();
        try
        {
            DeploymentResult cleanupResult = RestoreCore(
                launcherPaths,
                RestoreOperationKind.Cleanup,
                cancellationToken);
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

            using FileStream journal = DeploymentStateStore.OpenJournal(paths.JournalPath);
            string gameRoot = PhysicalDirectoryPath.ResolveExisting(launcherPaths.GameDirectory);
            string gameRootIdentity = DeploymentStateStore.GetGameRootIdentity(gameRoot);
            DeploymentStateStore.AppendJournalDurably(
                journal,
                DeploymentJournalRecord.DeploymentStarted(
                    deploymentId,
                    gameRoot,
                    gameRootIdentity,
                    launcherPaths.Game));

            IReadOnlyList<ResolvedDeploymentFile> files =
                DeploymentFilePlanner.ResolveDeploymentFiles(packages);
            sourceStreams.AddRange(files.Select(file => DeploymentFileTransaction.OpenDeploymentSource(file.SourcePath)));
            var sourceFingerprints = new DeploymentFileFingerprint[files.Count];
            Parallel.For(
                0,
                files.Count,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4)
                },
                index =>
                    sourceFingerprints[index] = DeploymentFileTransaction.ComputeFileFingerprint(sourceStreams[index]));
            (List<DeploymentFileDocument> Entries, HashSet<string> CreatedDirectories) deployment =
                ApplyDeploymentFiles(
                    launcherPaths,
                    disabledTargetRelativePaths,
                    paths,
                    journal,
                    deploymentId,
                    files,
                    sourceStreams,
                    sourceFingerprints,
                    cancellationToken);

            DeploymentManifestDocument document = new(
                DeploymentStateStore.CurrentSchemaVersion,
                deploymentId,
                deployment.Entries,
                deployment.CreatedDirectories.OrderByDescending(path => path.Length).ToList(),
                gameRoot,
                gameRootIdentity,
                launcherPaths.Game);
            cancellationToken.ThrowIfCancellationRequested();
            DeploymentStateStore.FlushJournal(journal);
            DeploymentStateStore.WriteManifest(paths.ActiveManifestPath, document);
            _logger.LogInformation(
                "Prepared deployment {DeploymentId} with {FileCount} file(s) in {ElapsedMilliseconds} ms.",
                deploymentId,
                deployment.Entries.Count,
                (long)Stopwatch.GetElapsedTime(preparationStartedTimestamp).TotalMilliseconds);
            return DeploymentResult.Success();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Deployment preparation was canceled; recovering any partial game-folder mutation.");
            RestoreCore(launcherPaths, RestoreOperationKind.Recovery, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Deployment preparation failed.");
            DeploymentFailure prepareFailure = new(
                DeploymentFailureKind.FileSystem,
                launcherPaths.GameDirectory,
                exception.Message);

            DeploymentResult recoveryResult;
            try
            {
                recoveryResult = RestoreCore(
                    launcherPaths,
                    RestoreOperationKind.Recovery,
                    CancellationToken.None);
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
                            recoveryException.Message)
                    });
            }

            if (recoveryResult.Succeeded)
            {
                return DeploymentResult.Failure(new[] { prepareFailure });
            }

            return DeploymentResult.Failure(
                new[] { prepareFailure }.Concat(recoveryResult.Failures).ToArray());
        }
        finally
        {
            foreach (FileStream sourceStream in sourceStreams)
            {
                sourceStream.Dispose();
            }
        }
    }

    /// <summary>
    ///     Applies every planned file mutation and returns the state that must be committed to the active manifest.
    /// </summary>
    private (List<DeploymentFileDocument> Entries, HashSet<string> CreatedDirectories) ApplyDeploymentFiles(
        LauncherPaths launcherPaths,
        IReadOnlyList<string> disabledTargetRelativePaths,
        DeploymentStatePaths paths,
        FileStream journal,
        string deploymentId,
        IReadOnlyList<ResolvedDeploymentFile> files,
        IReadOnlyList<FileStream> sourceStreams,
        IReadOnlyList<DeploymentFileFingerprint> sourceFingerprints,
        CancellationToken cancellationToken)
    {
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
            journal,
            deploymentId,
            deployedTargetPaths,
            backedUpTargetPaths,
            entries,
            cancellationToken);

        for (int fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolvedDeploymentFile file = files[fileIndex];
            string targetPath = DeploymentPathResolver.ResolveGamePath(launcherPaths, file.TargetRelativePath);
            EnsureTargetDirectory(launcherPaths, targetPath, journal, createdDirectories);

            DeploymentFileTransaction.EnsureSafeGameMutationPath(launcherPaths, targetPath);
            if (!backedUpTargetPaths.TryGetValue(file.TargetRelativePath, out DeploymentBackupDocument? backup) &&
                File.Exists(targetPath))
            {
                backup = _fileTransaction.BackupTargetFile(
                    paths,
                    journal,
                    deploymentId,
                    file.TargetRelativePath,
                    targetPath);
                backedUpTargetPaths[file.TargetRelativePath] = backup;
            }

            FileStream source = sourceStreams[fileIndex];
            DeploymentFileFingerprint sourceFingerprint = sourceFingerprints[fileIndex];
            string stagingPath = DeploymentFileTransaction.CreateSiblingStagingPath(
                targetPath,
                deploymentId,
                "deploy");
            string stagingRelativePath = DeploymentPathResolver.ToRelativeManifestPath(
                launcherPaths.GameDirectory,
                stagingPath);
            DeploymentStateStore.AppendJournalDurably(journal, DeploymentJournalRecord.FileDeploymentStarted(
                file.TargetRelativePath,
                backup?.RelativePath,
                sourceFingerprint,
                backup?.Fingerprint,
                stagingRelativePath));

            DeploymentFileTransaction.EnsureSafeGameMutationPath(launcherPaths, targetPath);
            (DeploymentMethod Method, DeploymentFileFingerprint Fingerprint, string? FileIdentity) deployedFile =
                _fileTransaction.DeployFile(
                    source,
                    file.SourcePath,
                    targetPath,
                    stagingPath,
                    sourceFingerprint);
            DeploymentStateStore.AppendJournal(journal, DeploymentJournalRecord.FileDeployed(
                file.TargetRelativePath,
                deployedFile.Method,
                backup?.RelativePath,
                deployedFile.Fingerprint,
                backup?.Fingerprint,
                stagingRelativePath,
                deployedFile.FileIdentity));
            cancellationToken.ThrowIfCancellationRequested();

            entries.Add(new DeploymentFileDocument(
                file.TargetRelativePath,
                deployedFile.Method,
                backup?.RelativePath,
                deployedFile.Fingerprint,
                backup?.Fingerprint,
                stagingRelativePath,
                backup?.StagingRelativePath,
                DeployedFileIdentity: deployedFile.FileIdentity));
        }

        return (entries, createdDirectories);
    }

    private static void EnsureTargetDirectory(
        LauncherPaths launcherPaths,
        string targetPath,
        FileStream journal,
        HashSet<string> createdDirectories)
    {
        DeploymentFileTransaction.EnsureSafeGameMutationPath(launcherPaths, targetPath);
        string targetDirectory = Path.GetDirectoryName(targetPath) ?? launcherPaths.GameDirectory;
        foreach (string directory in DeploymentFilePlanner.GetDirectoriesToCreate(
                     launcherPaths.GameDirectory,
                     targetDirectory))
        {
            if (Directory.Exists(directory))
            {
                continue;
            }

            DeploymentFileTransaction.EnsureSafeGameMutationPath(launcherPaths, directory);
            string relativeDirectory = DeploymentPathResolver.ToRelativeManifestPath(
                launcherPaths.GameDirectory,
                directory);
            DeploymentStateStore.AppendJournalDurably(
                journal,
                DeploymentJournalRecord.DirectoryCreated(relativeDirectory));
            Directory.CreateDirectory(directory);
            DeploymentFileTransaction.EnsureSafeGameMutationPath(launcherPaths, directory);
            createdDirectories.Add(relativeDirectory);
        }
    }

    private DeploymentResult ExecuteLocked(
        LauncherPaths paths,
        Func<DeploymentResult> operation,
        DeploymentFailureKind failureKind,
        Action<Exception> logFailure)
    {
        try
        {
            using FileStream deploymentLock = DeploymentStateStore.AcquireDeploymentLock(paths);
            return operation();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logFailure(exception);
            return DeploymentResult.Failure(
                new[]
                {
                    new DeploymentFailure(
                        failureKind,
                        paths.GameDirectory,
                        exception.Message)
                });
        }
    }

    /// <summary>
    ///     Restores a deployment during explicit cleanup or interrupted-state recovery while the operation lock is held.
    /// </summary>
    private DeploymentResult RestoreCore(
        LauncherPaths paths,
        RestoreOperationKind operationKind,
        CancellationToken cancellationToken)
    {
        long restoreStartedTimestamp = Stopwatch.GetTimestamp();
        DeploymentManifestDocument? manifest = RestoreActiveDeployment(paths, cancellationToken);
        if (manifest is null)
        {
            return DeploymentResult.Success();
        }

        if (operationKind == RestoreOperationKind.Recovery)
        {
            _logger.LogInformation("Recovered deployment state for {DeploymentId}.", manifest.DeploymentId);
        }
        else
        {
            _logger.LogInformation(
                "Cleaned deployment {DeploymentId} in {ElapsedMilliseconds} ms.",
                manifest.DeploymentId,
                (long)Stopwatch.GetElapsedTime(restoreStartedTimestamp).TotalMilliseconds);
        }

        return DeploymentResult.Success();
    }

    /// <summary>
    ///     Restores game files and removes the durable state for one active or interrupted deployment.
    /// </summary>
    private DeploymentManifestDocument? RestoreActiveDeployment(
        LauncherPaths paths,
        CancellationToken cancellationToken)
    {
        DeploymentStatePaths deploymentPaths = DeploymentStateStore.CreatePaths(paths, string.Empty);
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
    ///     Backs up deployment targets that should be hidden without deploying a replacement file.
    /// </summary>
    private void BackupDisabledTargets(
        LauncherPaths launcherPaths,
        IReadOnlyList<string> disabledTargetRelativePaths,
        DeploymentStatePaths deploymentPaths,
        FileStream journal,
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
            DeploymentFileTransaction.EnsureSafeGameMutationPath(launcherPaths, targetPath);
            if (!File.Exists(targetPath))
            {
                _logger.LogDebug(
                    "Skipped disabling base game file {FileName} because it does not exist.",
                    Path.GetFileName(targetPath));
                continue;
            }

            DeploymentBackupDocument backup = _fileTransaction.BackupTargetFile(
                deploymentPaths,
                journal,
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
                    null,
                    backup.Fingerprint,
                    null,
                    backup.StagingRelativePath));
            }

            _logger.LogInformation(
                "Temporarily disabled base game file {FileName} for modded launch deployment.",
                Path.GetFileName(targetPath));
        }
    }

    /// <summary>
    ///     Replays persisted deployment state to remove deployed files and restore original backups.
    /// </summary>
    private void CleanupManifest(
        LauncherPaths paths,
        DeploymentStatePaths deploymentPaths,
        DeploymentManifestDocument manifest,
        CancellationToken cancellationToken)
    {
        using FileStream journal = DeploymentStateStore.OpenJournal(deploymentPaths.JournalPath);
        foreach (DeploymentFileDocument file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _fileTransaction.RestoreFile(paths, deploymentPaths, manifest, file, journal);
        }

        foreach (string relativeDirectory in manifest.CreatedDirectories.OrderByDescending(path => path.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string directoryPath = DeploymentPathResolver.ResolveGamePath(paths, relativeDirectory);
            DeploymentFileTransaction.EnsureSafeGameMutationPath(paths, directoryPath);
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
            new OwnedContentPath(
                deploymentPaths.DeploymentDirectory,
                backupRoot));
    }

    private enum RestoreOperationKind
    {
        Cleanup,
        Recovery
    }
}
