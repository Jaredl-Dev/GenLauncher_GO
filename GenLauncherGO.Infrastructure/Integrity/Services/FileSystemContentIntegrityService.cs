using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Integrity.Contracts;
using GenLauncherGO.Infrastructure.Integrity.Support;
using GenLauncherGO.Infrastructure.Persistence.Services;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Integrity.Services;

/// <summary>
/// Verifies launcher-owned content with SHA-256 snapshots and applies confirmed managed-content cleanup.
/// </summary>
internal sealed class FileSystemContentIntegrityService : IContentIntegrityService
{
    private readonly ILogger<FileSystemContentIntegrityService> _logger;

    private readonly IAtomicFileWriter _atomicFileWriter;

    public FileSystemContentIntegrityService(
        IAtomicFileWriter atomicFileWriter,
        ILogger<FileSystemContentIntegrityService> logger)
    {
        _atomicFileWriter = atomicFileWriter ?? throw new ArgumentNullException(nameof(atomicFileWriter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ContentIntegrityReport> VerifyAsync(
        LauncherPaths paths,
        IReadOnlyList<ContentIntegrityTarget> targets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(targets);
        ContentIntegritySnapshotStore snapshotStore = CreateSnapshotStore(paths);

        if (targets.Count > 0)
        {
            _logger.LogInformation(
                "Starting content integrity verification for {TargetCount} target(s).",
                targets.Count);
        }
        else
        {
            _logger.LogDebug("Skipped content integrity verification because no targets were supplied.");
        }

        List<ContentIntegrityIssue> issues = new();
        foreach (ContentIntegrityTarget target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ContentIntegritySnapshotDocument? snapshot;
            try
            {
                snapshot = await snapshotStore.ReadSnapshotAsync(target.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                _logger.LogError(
                    exception,
                    "Failed to read integrity snapshot for {TargetName}.",
                    target.DisplayName);
                issues.Add(CreateIssue(
                    target,
                    IntegrityIssueKind.VerificationError,
                    IntegrityIssueAction.Block,
                    ".",
                    exception.Message));
                continue;
            }

            if (snapshot is null || snapshot.SourceKind != target.SourceKind)
            {
                _logger.LogWarning(
                    "Content integrity target {TargetName} is untracked or has changed source kind. Current source kind: {SourceKind}.",
                    target.DisplayName,
                    target.SourceKind);
                issues.Add(CreateIssue(
                    target,
                    IntegrityIssueKind.Untracked,
                    GetUntrackedAction(target.SourceKind),
                    "."));

                try
                {
                    ContentIntegrityScanResult untrackedScan =
                        await ContentIntegrityScanner.ScanAsync(target, cancellationToken).ConfigureAwait(false);
                    AddScanSafetyIssues(target, untrackedScan, issues);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    _logger.LogError(
                        exception,
                        "Failed to verify untracked content safety for {TargetName}.",
                        target.DisplayName);
                    issues.Add(CreateIssue(
                        target,
                        IntegrityIssueKind.VerificationError,
                        IntegrityIssueAction.Block,
                        ".",
                        exception.Message));
                }

                continue;
            }

            try
            {
                ContentIntegrityScanResult scan =
                    await ContentIntegrityScanner.ScanAsync(target, cancellationToken).ConfigureAwait(false);
                AddScanIssues(target, snapshot, scan, issues);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _logger.LogError(
                    exception,
                    "Failed to verify content for {TargetName}.",
                    target.DisplayName);
                issues.Add(CreateIssue(
                    target,
                    IntegrityIssueKind.VerificationError,
                    IntegrityIssueAction.Block,
                    ".",
                    exception.Message));
            }
        }

        if (targets.Count > 0)
        {
            _logger.LogInformation(
                "Completed content integrity verification for {TargetCount} target(s); issues: {IssueCount}.",
                targets.Count,
                issues.Count);
        }

        return new ContentIntegrityReport(issues);
    }

    public async Task<bool> CaptureSnapshotIfMatchesExpectedFileSetAsync(
        LauncherPaths paths,
        ContentIntegrityTarget target,
        IReadOnlySet<string> expectedRelativePaths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(expectedRelativePaths);
        ContentIntegritySnapshotStore snapshotStore = CreateSnapshotStore(paths);

        ContentIntegrityScanResult scan =
            await ContentIntegrityScanner.ScanAsync(target, cancellationToken).ConfigureAwait(false);
        if (!ContentIntegrityScanner.MatchesExpectedFileSet(scan, expectedRelativePaths))
        {
            _logger.LogWarning(
                "Skipped integrity snapshot capture for {TargetName} because the scanned file set did not match the expected package manifest.",
                target.DisplayName);
            return false;
        }

        await snapshotStore.WriteSnapshotAsync(target, scan, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task CaptureSnapshotAsync(
        LauncherPaths paths,
        ContentIntegrityTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(target);
        ContentIntegritySnapshotStore snapshotStore = CreateSnapshotStore(paths);

        ContentIntegrityScanResult scan =
            await ContentIntegrityScanner.ScanAsync(target, cancellationToken).ConfigureAwait(false);
        if (scan.UnsafeLinks.Count > 0 || scan.Errors.Count > 0)
        {
            _logger.LogWarning(
                "Blocked integrity snapshot capture for {TargetName}; unsafe links: {UnsafeLinkCount}; errors: {ErrorCount}.",
                target.DisplayName,
                scan.UnsafeLinks.Count,
                scan.Errors.Count);
            throw new IOException("Content containing unsafe links or unreadable entries cannot be trusted.");
        }

        await snapshotStore.WriteSnapshotAsync(target, scan, cancellationToken).ConfigureAwait(false);
    }

    public Task ApplyCleanupAsync(
        ContentIntegrityReport report,
        IReadOnlyList<ContentIntegrityTarget> targets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(targets);

        var targetIndex =
            targets.ToDictionary(target => target.Id, StringComparer.Ordinal);
        int deleteIssueCount = report.Issues.Count(issue => issue.Action == IntegrityIssueAction.Delete);

        _logger.LogInformation(
            "Applying content integrity cleanup for {DeleteIssueCount} delete issue(s).",
            deleteIssueCount);

        foreach (ContentIntegrityIssue issue in report.Issues.Where(issue =>
                     issue.Action == IntegrityIssueAction.Delete))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!targetIndex.TryGetValue(issue.TargetId, out ContentIntegrityTarget? target))
            {
                throw new InvalidDataException("The cleanup report references an unknown integrity target.");
            }

            string path = ContentIntegrityPath.ResolveRelativePath(target.RootDirectory, issue.RelativePath);
            string? parentDirectory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parentDirectory))
            {
                FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
                    parentDirectory,
                    "Integrity cleanup paths must be rooted.",
                    "Integrity cleanup paths must not cross reparse points.");
            }

            DeleteEntry(path, issue, target);
        }

        foreach (ContentIntegrityTarget target in targets.Where(target =>
                     report.Issues.Any(issue =>
                         issue.TargetId == target.Id &&
                         issue.Action == IntegrityIssueAction.Delete)))
        {
            DeleteUnexpectedEmptyDirectories(target, cancellationToken);
        }

        _logger.LogInformation(
            "Completed content integrity cleanup for {DeleteIssueCount} delete issue(s).",
            deleteIssueCount);
        return Task.CompletedTask;
    }

    private ContentIntegritySnapshotStore CreateSnapshotStore(LauncherPaths paths)
    {
        return new ContentIntegritySnapshotStore(
            paths.IntegrityDirectory,
            _atomicFileWriter,
            _logger);
    }

    private static void AddScanIssues(
        ContentIntegrityTarget target,
        ContentIntegritySnapshotDocument snapshot,
        ContentIntegrityScanResult scan,
        List<ContentIntegrityIssue> issues)
    {
        var expectedFiles =
            snapshot.Files.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);

        foreach (ContentIntegritySnapshotFileEntry expected in expectedFiles.Values)
        {
            if (!scan.Files.TryGetValue(expected.RelativePath, out ContentIntegrityScannedFile? current))
            {
                issues.Add(CreateIssue(
                    target,
                    IntegrityIssueKind.MissingFile,
                    GetManagedOrManualAction(target.SourceKind, IntegrityIssueKind.MissingFile),
                    expected.RelativePath,
                    expectedSizeBytes: expected.Size));
                continue;
            }

            if (current.Size != expected.Size ||
                !string.Equals(current.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(CreateIssue(
                    target,
                    IntegrityIssueKind.ModifiedFile,
                    GetManagedOrManualAction(target.SourceKind, IntegrityIssueKind.ModifiedFile),
                    expected.RelativePath,
                    expectedSizeBytes: expected.Size));
            }
        }

        foreach (ContentIntegrityScannedFile current in scan.Files.Values)
        {
            if (!expectedFiles.ContainsKey(current.RelativePath))
            {
                issues.Add(CreateIssue(
                    target,
                    IntegrityIssueKind.UnexpectedFile,
                    GetManagedOrManualAction(target.SourceKind, IntegrityIssueKind.UnexpectedFile),
                    current.RelativePath));
            }
        }

        HashSet<string> expectedEmptyDirectories =
            new(snapshot.EmptyDirectories, StringComparer.OrdinalIgnoreCase);
        foreach (string directory in scan.EmptyDirectories)
        {
            if (!expectedEmptyDirectories.Contains(directory))
            {
                issues.Add(CreateIssue(
                    target,
                    IntegrityIssueKind.EmptyDirectory,
                    GetManagedOrManualAction(target.SourceKind, IntegrityIssueKind.EmptyDirectory),
                    directory));
            }
        }

        AddScanSafetyIssues(target, scan, issues);
    }

    private static void AddScanSafetyIssues(
        ContentIntegrityTarget target,
        ContentIntegrityScanResult scan,
        List<ContentIntegrityIssue> issues)
    {
        foreach (string unsafeLink in scan.UnsafeLinks)
        {
            issues.Add(CreateIssue(
                target,
                IntegrityIssueKind.UnsafeLink,
                target.SourceKind is ContentSourceKind.ManagedS3 or ContentSourceKind.ManagedSingleFile
                    ? IntegrityIssueAction.Delete
                    : IntegrityIssueAction.Block,
                unsafeLink));
        }

        foreach (ContentIntegrityScanError error in scan.Errors)
        {
            issues.Add(CreateIssue(
                target,
                IntegrityIssueKind.VerificationError,
                IntegrityIssueAction.Block,
                error.RelativePath,
                error.Message));
        }
    }

    private static ContentIntegrityIssue CreateIssue(
        ContentIntegrityTarget target,
        IntegrityIssueKind kind,
        IntegrityIssueAction action,
        string relativePath,
        string? message = null,
        long? expectedSizeBytes = null)
    {
        return new ContentIntegrityIssue(
            target.Id,
            target.DisplayName,
            target.SourceKind,
            kind,
            action,
            LexicalPath.NormalizeRelativePath(relativePath),
            message,
            expectedSizeBytes);
    }

    private static IntegrityIssueAction GetUntrackedAction(ContentSourceKind sourceKind)
    {
        return sourceKind switch
        {
            ContentSourceKind.ManagedS3 => IntegrityIssueAction.Repair,
            ContentSourceKind.ManagedSingleFile => IntegrityIssueAction.Redownload,
            ContentSourceKind.Manual => IntegrityIssueAction.Absorb,
            _ => IntegrityIssueAction.TrustAsManual,
        };
    }

    private static IntegrityIssueAction GetManagedOrManualAction(
        ContentSourceKind sourceKind,
        IntegrityIssueKind issueKind)
    {
        if (sourceKind == ContentSourceKind.Manual)
        {
            return IntegrityIssueAction.Absorb;
        }

        if (sourceKind == ContentSourceKind.ManagedSingleFile)
        {
            return issueKind is IntegrityIssueKind.UnexpectedFile or IntegrityIssueKind.EmptyDirectory
                ? IntegrityIssueAction.Delete
                : IntegrityIssueAction.Redownload;
        }

        if (sourceKind == ContentSourceKind.ManagedS3)
        {
            return issueKind is IntegrityIssueKind.UnexpectedFile or IntegrityIssueKind.EmptyDirectory
                ? IntegrityIssueAction.Delete
                : IntegrityIssueAction.Repair;
        }

        return IntegrityIssueAction.TrustAsManual;
    }

    /// <summary>
    /// Deletes one confirmed managed-content entry without following links.
    /// </summary>
    private void DeleteEntry(
        string path,
        ContentIntegrityIssue issue,
        ContentIntegrityTarget target)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            Directory.Delete(path, recursive: false);
        }
        else
        {
            File.Delete(path);
        }

        _logger.LogInformation(
            "Deleted confirmed unexpected integrity entry {RelativePath} from {TargetName}.",
            issue.RelativePath,
            target.DisplayName);
    }

    private void DeleteUnexpectedEmptyDirectories(
        ContentIntegrityTarget target,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(target.RootDirectory))
        {
            return;
        }

        if (FileSystemPathSafety.IsReparsePoint(target.RootDirectory))
        {
            return;
        }

        foreach (string directory in Directory
                     .EnumerateDirectories(
                         target.RootDirectory,
                         "*",
                         FileSystemPathSafety.CreateRecursiveNoLinksOptions())
                     .OrderByDescending(path => path.Length)
                     .ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = ContentIntegrityPath.GetRelativePath(target.RootDirectory, directory);
            if (ContentIntegrityPath.IsIgnored(target, relativePath) ||
                FileSystemPathSafety.IsReparsePoint(directory) ||
                Directory.EnumerateFileSystemEntries(directory).Any())
            {
                continue;
            }

            Directory.Delete(directory);
            _logger.LogInformation(
                "Deleted empty integrity cleanup directory {RelativePath} from {TargetName}.",
                relativePath,
                target.DisplayName);
        }
    }

}
