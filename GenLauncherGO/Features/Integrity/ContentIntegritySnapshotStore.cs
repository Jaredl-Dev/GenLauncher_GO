using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Shared.IO;
using GenLauncherGO.Shared.Persistence;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Integrity;

/// <summary>
///     Persists content integrity snapshots for verified targets.
/// </summary>
internal sealed class ContentIntegritySnapshotStore
{
    private const int SnapshotSchemaVersion = 1;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly IAtomicFileWriter _atomicFileWriter;

    private readonly ILogger _logger;

    private readonly string _snapshotDirectory;

    public ContentIntegritySnapshotStore(
        string snapshotDirectory,
        IAtomicFileWriter atomicFileWriter,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotDirectory);
        _snapshotDirectory = LexicalPath.NormalizeFullPath(snapshotDirectory);
        _atomicFileWriter = atomicFileWriter ?? throw new ArgumentNullException(nameof(atomicFileWriter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Writes a trusted snapshot for a previously completed safe scan.
    /// </summary>
    public async Task WriteSnapshotAsync(
        ContentIntegrityTarget target,
        ContentIntegrityScanResult scan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ContentIntegritySnapshotDocument snapshot = new(
            SnapshotSchemaVersion,
            target.Id,
            target.SourceKind,
            scan.Files.Values
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(file => new ContentIntegritySnapshotFileEntry(file.RelativePath, file.Size, file.Sha256))
                .ToList(),
            target.SourceKind == ContentSourceKind.Manual
                ? scan.EmptyDirectories
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : Array.Empty<string>());

        string snapshotPath = GetSnapshotPath(target.Id);
        await _atomicFileWriter.WriteAsync(
                snapshotPath,
                (stream, token) => JsonSerializer.SerializeAsync(stream, snapshot, _jsonOptions, token),
                cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "Captured SHA-256 integrity snapshot for {TargetName}; files: {FileCount}.",
            target.DisplayName,
            snapshot.Files.Count);
    }

    /// <summary>
    ///     Reads a persisted snapshot, returning <see langword="null" /> when none exists.
    /// </summary>
    public async Task<ContentIntegritySnapshotDocument?> ReadSnapshotAsync(
        string targetId,
        CancellationToken cancellationToken)
    {
        string path = GetSnapshotPath(targetId);
        if (!File.Exists(path))
        {
            _logger.LogDebug(
                "No integrity snapshot exists for target {TargetId}.",
                targetId);
            return null;
        }

        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        ContentIntegritySnapshotDocument? snapshot =
            await JsonSerializer.DeserializeAsync<ContentIntegritySnapshotDocument>(
                stream,
                _jsonOptions,
                cancellationToken).ConfigureAwait(false);
        if (snapshot is null ||
            snapshot.SchemaVersion != SnapshotSchemaVersion ||
            !string.Equals(snapshot.TargetId, targetId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Integrity snapshot for target {TargetId} has unsupported schema or ownership.",
                targetId);
            throw new InvalidDataException("The integrity snapshot schema or ownership is not supported.");
        }

        _logger.LogDebug(
            "Loaded integrity snapshot for target {TargetId}; files: {FileCount}.",
            targetId,
            snapshot.Files.Count);
        return snapshot;
    }

    /// <summary>
    ///     Deletes every persisted snapshot outside the retained set and reports how many were removed.
    /// </summary>
    /// <remarks>
    ///     Snapshot file names are a one-way hash of the target identifier, so retention is decided by hashing the
    ///     retained identifiers rather than by reading each document back.
    /// </remarks>
    public int DeleteSnapshotsExcept(IReadOnlySet<string> retainedTargetIds)
    {
        ArgumentNullException.ThrowIfNull(retainedTargetIds);

        if (!Directory.Exists(_snapshotDirectory))
        {
            return 0;
        }

        var retainedFileNames = retainedTargetIds
            .Select(GetSnapshotFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int deletedCount = 0;
        foreach (string snapshotPath in FileSystemPathSafety.GetDirectoryFilesWithNoReparsePoints(
                     _snapshotDirectory,
                     "Content integrity snapshot paths"))
        {
            if (retainedFileNames.Contains(Path.GetFileName(snapshotPath)))
            {
                continue;
            }

            File.Delete(snapshotPath);
            deletedCount++;
            _logger.LogDebug(
                "Deleted orphaned content integrity snapshot {SnapshotFileName}.",
                Path.GetFileName(snapshotPath));
        }

        return deletedCount;
    }

    private string GetSnapshotPath(string targetId)
    {
        return Path.Combine(_snapshotDirectory, GetSnapshotFileName(targetId));
    }

    private static string GetSnapshotFileName(string targetId)
    {
        byte[] identifierHash = SHA256.HashData(Encoding.UTF8.GetBytes(targetId));
        return Convert.ToHexString(identifierHash) + ".json";
    }
}

/// <summary>
///     Describes one trusted file entry in a snapshot document.
/// </summary>
internal sealed record ContentIntegritySnapshotFileEntry(string RelativePath, long Size, string Sha256);

/// <summary>
///     Describes a persisted trusted content snapshot.
/// </summary>
internal sealed record ContentIntegritySnapshotDocument(
    int SchemaVersion,
    string TargetId,
    ContentSourceKind SourceKind,
    IReadOnlyList<ContentIntegritySnapshotFileEntry> Files,
    IReadOnlyList<string> EmptyDirectories);
