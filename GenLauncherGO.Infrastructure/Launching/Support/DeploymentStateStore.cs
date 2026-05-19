using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Persistence.Services;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Launching.Support;

/// <summary>
///     Persists deployment manifest and journal state used to recover file-system deployment side effects.
/// </summary>
internal sealed class DeploymentStateStore
{
    public const string BackupsDirectoryName = "Backups";

    internal const int CurrentSchemaVersion = 2;

    private const string ActiveManifestFileName = "active.json";

    private const string JournalFileName = "journal.jsonl";

    private const string LockFileName = "deployment.lock";

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        // Schema-v2 manifests may contain retired reporting fields that are irrelevant to safe recovery.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions _journalJsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };

    private readonly ILogger _logger;

    public DeploymentStateStore(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static DeploymentStatePaths CreatePaths(LauncherPaths paths, string deploymentId)
    {
        string deploymentDirectory = paths.DeploymentDirectory;
        string backupDirectory = string.IsNullOrWhiteSpace(deploymentId)
            ? Path.Combine(deploymentDirectory, BackupsDirectoryName)
            : Path.Combine(deploymentDirectory, BackupsDirectoryName, deploymentId);

        return new DeploymentStatePaths(
            deploymentDirectory,
            Path.Combine(deploymentDirectory, ActiveManifestFileName),
            Path.Combine(deploymentDirectory, JournalFileName),
            Path.Combine(deploymentDirectory, LockFileName),
            backupDirectory);
    }

    /// <summary>
    ///     Holds an exclusive file lock so deployment preparation, cleanup, and recovery cannot overlap.
    /// </summary>
    public static FileStream AcquireDeploymentLock(LauncherPaths paths)
    {
        DeploymentStatePaths deploymentPaths = CreatePaths(paths, string.Empty);
        OwnedDirectoryTree.EnsureExists(paths.OwnedGameDataDirectory, deploymentPaths.DeploymentDirectory);
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            deploymentPaths.LockPath,
            "Deployment lock paths");

        return new FileStream(
            deploymentPaths.LockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
    }

    /// <summary>
    ///     Commits the completed deployment manifest with atomic replacement semantics.
    /// </summary>
    public static void WriteManifest(string manifestPath, DeploymentManifestDocument manifest)
    {
        new AtomicFileWriter().WriteText(manifestPath, JsonSerializer.Serialize(manifest, _jsonOptions));
    }

    /// <summary>
    ///     Deletes persisted active deployment state after cleanup or recovery.
    /// </summary>
    public static void DeleteDeploymentStateFiles(DeploymentStatePaths paths)
    {
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            paths.ActiveManifestPath,
            "Deployment manifest paths");
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            paths.JournalPath,
            "Deployment journal paths");

        if (File.Exists(paths.ActiveManifestPath))
        {
            File.Delete(paths.ActiveManifestPath);
        }

        if (File.Exists(paths.JournalPath))
        {
            File.Delete(paths.JournalPath);
        }
    }

    /// <summary>
    ///     Opens the append-only journal once for a deployment operation.
    /// </summary>
    public static FileStream OpenJournal(string journalPath)
    {
        string journalDirectory = Path.GetDirectoryName(journalPath)
                                  ?? throw new InvalidOperationException(
                                      "Deployment journal paths must have a parent directory.");
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            journalDirectory,
            "Deployment journal paths");
        Directory.CreateDirectory(journalDirectory);
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            journalPath,
            "Deployment journal paths");

        return new FileStream(
            journalPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            4096,
            FileOptions.None);
    }

    /// <summary>
    ///     Durably appends one standalone recovery record.
    /// </summary>
    public static void AppendJournal(string journalPath, DeploymentJournalRecord record)
    {
        using FileStream journal = OpenJournal(journalPath);
        AppendJournalDurably(journal, record);
    }

    /// <summary>
    ///     Appends a recovery record. A later durable intent flushes earlier completion records in order.
    /// </summary>
    public static void AppendJournal(FileStream journal, DeploymentJournalRecord record)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(record);

        byte[] bytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(record, _journalJsonOptions) + Environment.NewLine);
        journal.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    ///     Appends and commits an intent before its recoverable file-system mutation runs.
    /// </summary>
    public static void AppendJournalDurably(FileStream journal, DeploymentJournalRecord record)
    {
        AppendJournal(journal, record);
        FlushJournal(journal);
    }

    /// <summary>
    ///     Commits all appended recovery records through the storage device.
    /// </summary>
    public static void FlushJournal(FileStream journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        journal.Flush(true);
    }

    /// <summary>
    ///     Reads the active manifest or reconstructs it from the deployment journal.
    /// </summary>
    public DeploymentManifestDocument? ReadManifestOrJournal(
        LauncherPaths launcherPaths,
        DeploymentStatePaths paths)
    {
        DeploymentManifestDocument? manifest = TryReadManifest(paths.ActiveManifestPath, out Exception? readException);
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            paths.JournalPath,
            "Deployment journal paths");
        if (File.Exists(paths.JournalPath))
        {
            DeploymentManifestDocument? journalManifest = RebuildManifestFromJournal(paths);
            if (journalManifest is not null)
            {
                ValidateGameRoot(launcherPaths, journalManifest);
                return journalManifest;
            }

            if (manifest is null && readException is not null)
            {
                throw new InvalidDataException(
                    "The deployment manifest could not be read and the journal did not contain recoverable deployment state.",
                    readException);
            }

            if (manifest is not null)
            {
                ValidateGameRoot(launcherPaths, manifest);
            }

            return manifest;
        }

        if (manifest is not null)
        {
            ValidateGameRoot(launcherPaths, manifest);
            return manifest;
        }

        if (readException is not null)
        {
            throw new InvalidDataException(
                "The deployment manifest could not be read and no journal was available for recovery.",
                readException);
        }

        return null;
    }

    /// <summary>
    ///     Tries to read a completed deployment manifest without preventing journal fallback.
    /// </summary>
    private DeploymentManifestDocument? TryReadManifest(string manifestPath, out Exception? readException)
    {
        readException = null;
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            manifestPath,
            "Deployment manifest paths");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            DeploymentManifestDocument? manifest =
                JsonSerializer.Deserialize<DeploymentManifestDocument>(File.ReadAllText(manifestPath), _jsonOptions);
            if (manifest is null)
            {
                readException = new InvalidDataException("The deployment manifest did not contain manifest data.");
                _logger.LogWarning(
                    "Deployment manifest did not contain manifest data; journal recovery will be attempted.");
            }

            return manifest;
        }
        catch (Exception exception) when (exception is IOException or JsonException or NotSupportedException)
        {
            readException = exception;
            _logger.LogWarning(exception, "Deployment manifest could not be read; journal recovery will be attempted.");
            return null;
        }
    }

    /// <summary>
    ///     Rebuilds the effective deployment state from intent and completion records after an interrupted mutation.
    /// </summary>
    private DeploymentManifestDocument? RebuildManifestFromJournal(DeploymentStatePaths paths)
    {
        var filesByTarget = new Dictionary<string, DeploymentFileDocument>(StringComparer.OrdinalIgnoreCase);
        var backupsByTarget = new Dictionary<string, DeploymentBackupDocument>(StringComparer.OrdinalIgnoreCase);
        var backupStartsByTarget = new Dictionary<string, DeploymentBackupDocument>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool sawDeploymentStateRecord = false;
        string? deploymentId = null;
        string? gameRoot = null;
        string? gameRootIdentity = null;
        SupportedGame game = SupportedGame.Unknown;

        foreach (string line in File.ReadLines(paths.JournalPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            DeploymentJournalRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<DeploymentJournalRecord>(line, _journalJsonOptions);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Skipped unreadable deployment journal record.");
                continue;
            }

            if (record is null)
            {
                continue;
            }

            if (string.Equals(record.Action, DeploymentJournalRecord.DeploymentStartedAction, StringComparison.Ordinal))
            {
                sawDeploymentStateRecord = true;
                deploymentId = record.DeploymentId;
                gameRoot = record.GameRoot;
                gameRootIdentity = record.GameRootIdentity;
                game = record.Game;
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.TargetRelativePath))
            {
                _logger.LogWarning("Skipped deployment journal record without a target path.");
                continue;
            }

            switch (record.Action)
            {
                case DeploymentJournalRecord.DirectoryCreatedAction:
                    sawDeploymentStateRecord = true;
                    directories.Add(record.TargetRelativePath);
                    break;

                case DeploymentJournalRecord.FileBackupStartedAction:
                    sawDeploymentStateRecord = true;
                    if (!string.IsNullOrWhiteSpace(record.BackupRelativePath))
                    {
                        var backup = new DeploymentBackupDocument(
                            record.BackupRelativePath,
                            record.BackupFingerprint,
                            record.StagingRelativePath);
                        backupStartsByTarget[record.TargetRelativePath] = backup;
                        backupsByTarget[record.TargetRelativePath] = backup;
                    }

                    break;

                case DeploymentJournalRecord.FileBackedUpAction:
                {
                    sawDeploymentStateRecord = true;
                    var backup = new DeploymentBackupDocument(
                        record.BackupRelativePath ?? string.Empty,
                        record.BackupFingerprint,
                        record.StagingRelativePath);
                    backupsByTarget[record.TargetRelativePath] = backup;
                    filesByTarget[record.TargetRelativePath] = new DeploymentFileDocument(
                        record.TargetRelativePath,
                        DeploymentMethod.Copy,
                        record.BackupRelativePath,
                        null,
                        record.BackupFingerprint,
                        null,
                        record.StagingRelativePath);
                    break;
                }

                case DeploymentJournalRecord.FileDeploymentStartedAction:
                {
                    sawDeploymentStateRecord = true;
                    string targetRelativePath = record.TargetRelativePath;
                    backupsByTarget.TryGetValue(targetRelativePath, out DeploymentBackupDocument? backup);
                    filesByTarget[targetRelativePath] = new DeploymentFileDocument(
                        targetRelativePath,
                        // The hard-link attempt happens after this intent record. Treat an interrupted operation as a
                        // potential hard link so recovery never clears a shared ReadOnly attribute through the target.
                        DeploymentMethod.HardLink,
                        record.BackupRelativePath ?? backup?.RelativePath,
                        record.DeployedFingerprint,
                        record.BackupFingerprint ?? backup?.Fingerprint,
                        record.StagingRelativePath,
                        backup?.StagingRelativePath);
                    break;
                }

                case DeploymentJournalRecord.FileDeployedAction:
                {
                    sawDeploymentStateRecord = true;
                    string targetRelativePath = record.TargetRelativePath;
                    backupsByTarget.TryGetValue(targetRelativePath, out DeploymentBackupDocument? backup);
                    filesByTarget[targetRelativePath] = new DeploymentFileDocument(
                        targetRelativePath,
                        record.Method ?? DeploymentMethod.Copy,
                        record.BackupRelativePath ?? backup?.RelativePath,
                        record.DeployedFingerprint,
                        record.BackupFingerprint ?? backup?.Fingerprint,
                        record.StagingRelativePath,
                        backup?.StagingRelativePath,
                        DeployedFileIdentity: record.DeployedFileIdentity);
                    break;
                }

                case DeploymentJournalRecord.FileCleanupDeleteCompletedAction:
                case DeploymentJournalRecord.FileCleanupRestoredAction:
                    sawDeploymentStateRecord = true;
                    filesByTarget.Remove(record.TargetRelativePath);
                    break;

                case DeploymentJournalRecord.FileCleanupRestoreStartedAction:
                    sawDeploymentStateRecord = true;
                    if (filesByTarget.TryGetValue(
                            record.TargetRelativePath,
                            out DeploymentFileDocument? restoringFile))
                    {
                        filesByTarget[record.TargetRelativePath] = restoringFile with
                        {
                            RestoreStagingRelativePath = record.StagingRelativePath
                        };
                    }

                    break;
            }
        }

        foreach (KeyValuePair<string, DeploymentBackupDocument> backupStart in backupStartsByTarget)
        {
            if (filesByTarget.ContainsKey(backupStart.Key))
            {
                continue;
            }

            string backupPath = DeploymentPathResolver.ResolveDeploymentStatePath(
                paths.DeploymentDirectory,
                backupStart.Value.RelativePath);
            if (!File.Exists(backupPath))
            {
                DeleteIncompleteLauncherBackup(paths, backupStart.Value.StagingRelativePath);
                continue;
            }

            filesByTarget[backupStart.Key] = new DeploymentFileDocument(
                backupStart.Key,
                DeploymentMethod.Copy,
                backupStart.Value.RelativePath,
                null,
                backupStart.Value.Fingerprint,
                null,
                backupStart.Value.StagingRelativePath);
        }

        if (filesByTarget.Count == 0 && directories.Count == 0 && !sawDeploymentStateRecord)
        {
            return null;
        }

        return new DeploymentManifestDocument(
            CurrentSchemaVersion,
            deploymentId ?? "recovered",
            filesByTarget.Values.ToList(),
            directories.OrderByDescending(path => path.Length).ToList(),
            gameRoot,
            gameRootIdentity,
            game);
    }

    private static void DeleteIncompleteLauncherBackup(
        DeploymentStatePaths paths,
        string? stagingRelativePath)
    {
        if (string.IsNullOrWhiteSpace(stagingRelativePath))
        {
            return;
        }

        string stagingPath = DeploymentPathResolver.ResolveDeploymentStatePath(
            paths.DeploymentDirectory,
            stagingRelativePath);
        stagingPath = FileSystemPathSafety.ResolveOwnedSubpath(
            paths.DeploymentDirectory,
            stagingPath,
            "Deployment backup staging paths",
            "the deployment directory");
        if (File.Exists(stagingPath))
        {
            FileAttributes attributes = File.GetAttributes(stagingPath);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(stagingPath, attributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(stagingPath);
        }
    }

    /// <summary>
    ///     Refuses to replay durable state against a different game installation.
    /// </summary>
    private static void ValidateGameRoot(LauncherPaths paths, DeploymentManifestDocument manifest)
    {
        if (manifest.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException("The deployment state schema is not supported.");
        }

        if (manifest.Game != paths.Game)
        {
            throw new InvalidDataException("Deployment state belongs to a different supported game.");
        }

        if (string.IsNullOrWhiteSpace(manifest.GameRoot) ||
            !LexicalPath.AreEquivalent(
                manifest.GameRoot,
                PhysicalDirectoryPath.ResolveExisting(paths.GameDirectory)))
        {
            throw new InvalidDataException("Deployment state belongs to a different game directory.");
        }

        if (string.IsNullOrWhiteSpace(manifest.GameRootIdentity) ||
            !string.Equals(
                manifest.GameRootIdentity,
                GetGameRootIdentity(paths.GameDirectory),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The game directory changed after deployment state was recorded.");
        }
    }

    internal static string GetGameRootIdentity(string gameDirectory)
    {
        return FormatIdentity(PhysicalDirectoryPath.GetIdentity(gameDirectory));
    }

    internal static string GetFileIdentity(string filePath)
    {
        return FormatIdentity(PhysicalDirectoryPath.GetFileIdentity(filePath));
    }

    private static string FormatIdentity(PhysicalFileSystemIdentity identity)
    {
        return $"{identity.VolumeSerialNumber:X8}:{identity.FileIndex:X16}";
    }
}

internal sealed record DeploymentStatePaths(
    string DeploymentDirectory,
    string ActiveManifestPath,
    string JournalPath,
    string LockPath,
    string BackupDirectory);

/// <summary>
///     Defines the versioned manifest persisted for deployment cleanup and recovery.
/// </summary>
internal sealed record DeploymentManifestDocument(
    int SchemaVersion,
    string DeploymentId,
    IReadOnlyList<DeploymentFileDocument> Files,
    IReadOnlyList<string> CreatedDirectories,
    string? GameRoot = null,
    string? GameRootIdentity = null,
    SupportedGame Game = SupportedGame.Unknown);

/// <summary>
///     Defines one file-system mutation persisted in a deployment manifest.
/// </summary>
internal sealed record DeploymentFileDocument(
    string TargetRelativePath,
    DeploymentMethod Method,
    string? BackupRelativePath,
    DeploymentFileFingerprint? DeployedFingerprint = null,
    DeploymentFileFingerprint? BackupFingerprint = null,
    string? StagingRelativePath = null,
    string? BackupStagingRelativePath = null,
    string? RestoreStagingRelativePath = null,
    string? DeployedFileIdentity = null);

/// <summary>
///     Identifies exact file bytes that a deployment transaction may safely remove or replace.
/// </summary>
internal sealed record DeploymentFileFingerprint(long Length, string Sha256);

internal sealed record DeploymentBackupDocument(
    string RelativePath,
    DeploymentFileFingerprint? Fingerprint,
    string? StagingRelativePath);

/// <summary>
///     Defines one intent or completion record in the append-only recovery journal.
/// </summary>
internal sealed record DeploymentJournalRecord(
    string Action,
    string? TargetRelativePath,
    string? BackupRelativePath,
    DeploymentMethod? Method,
    string? DeploymentId = null,
    string? GameRoot = null,
    string? GameRootIdentity = null,
    DeploymentFileFingerprint? DeployedFingerprint = null,
    DeploymentFileFingerprint? BackupFingerprint = null,
    string? StagingRelativePath = null,
    SupportedGame Game = SupportedGame.Unknown,
    string? DeployedFileIdentity = null)
{
    public const string DeploymentStartedAction = "deployment-started";

    public const string DirectoryCreatedAction = "directory-created";

    public const string FileBackupStartedAction = "file-backup-started";

    public const string FileBackedUpAction = "file-backed-up";

    public const string FileDeploymentStartedAction = "file-deployment-started";

    public const string FileDeployedAction = "file-deployed";

    public const string FileCleanupDeleteCompletedAction = "file-cleanup-delete-completed";

    public const string FileCleanupRestoreStartedAction = "file-cleanup-restore-started";

    public const string FileCleanupRestoredAction = "file-cleanup-restored";

    public static DeploymentJournalRecord DeploymentStarted(
        string deploymentId,
        string gameRoot,
        string gameRootIdentity,
        SupportedGame game)
    {
        return new DeploymentJournalRecord(
            DeploymentStartedAction,
            null,
            null,
            null,
            deploymentId,
            gameRoot,
            gameRootIdentity,
            Game: game);
    }

    public static DeploymentJournalRecord DirectoryCreated(string targetRelativePath)
    {
        return new DeploymentJournalRecord(DirectoryCreatedAction, targetRelativePath, null, null);
    }

    public static DeploymentJournalRecord FileBackupStarted(
        string targetRelativePath,
        string backupRelativePath,
        string stagingRelativePath)
    {
        return new DeploymentJournalRecord(
            FileBackupStartedAction,
            targetRelativePath,
            backupRelativePath,
            null,
            StagingRelativePath: stagingRelativePath);
    }

    public static DeploymentJournalRecord FileBackedUp(
        string targetRelativePath,
        string backupRelativePath,
        DeploymentFileFingerprint backupFingerprint,
        string stagingRelativePath)
    {
        return new DeploymentJournalRecord(
            FileBackedUpAction,
            targetRelativePath,
            backupRelativePath,
            null,
            BackupFingerprint: backupFingerprint,
            StagingRelativePath: stagingRelativePath);
    }

    public static DeploymentJournalRecord FileDeploymentStarted(
        string targetRelativePath,
        string? backupRelativePath,
        DeploymentFileFingerprint deployedFingerprint,
        DeploymentFileFingerprint? backupFingerprint,
        string stagingRelativePath)
    {
        return new DeploymentJournalRecord(
            FileDeploymentStartedAction,
            targetRelativePath,
            backupRelativePath,
            null,
            DeployedFingerprint: deployedFingerprint,
            BackupFingerprint: backupFingerprint,
            StagingRelativePath: stagingRelativePath);
    }

    public static DeploymentJournalRecord FileDeployed(
        string targetRelativePath,
        DeploymentMethod method,
        string? backupRelativePath,
        DeploymentFileFingerprint deployedFingerprint,
        DeploymentFileFingerprint? backupFingerprint,
        string stagingRelativePath,
        string? deployedFileIdentity = null)
    {
        return new DeploymentJournalRecord(
            FileDeployedAction,
            targetRelativePath,
            backupRelativePath,
            method,
            DeployedFingerprint: deployedFingerprint,
            BackupFingerprint: backupFingerprint,
            StagingRelativePath: stagingRelativePath,
            DeployedFileIdentity: deployedFileIdentity);
    }

    public static DeploymentJournalRecord FileCleanupDeleted(string targetRelativePath)
    {
        return new DeploymentJournalRecord(
            FileCleanupDeleteCompletedAction,
            targetRelativePath,
            null,
            null);
    }

    public static DeploymentJournalRecord FileCleanupRestoreStarted(
        string targetRelativePath,
        string backupRelativePath,
        string stagingRelativePath)
    {
        return new DeploymentJournalRecord(
            FileCleanupRestoreStartedAction,
            targetRelativePath,
            backupRelativePath,
            null,
            StagingRelativePath: stagingRelativePath);
    }

    public static DeploymentJournalRecord FileCleanupRestored(string targetRelativePath, string backupRelativePath)
    {
        return new DeploymentJournalRecord(
            FileCleanupRestoredAction,
            targetRelativePath,
            backupRelativePath,
            null);
    }
}
