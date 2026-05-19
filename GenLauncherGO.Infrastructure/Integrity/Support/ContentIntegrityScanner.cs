using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Infrastructure.Common;

namespace GenLauncherGO.Infrastructure.Integrity.Support;

/// <summary>
///     Scans content integrity targets without following reparse points.
/// </summary>
internal static class ContentIntegrityScanner
{
    private const int MaxConcurrentFileHashes = 4;

    /// <summary>
    ///     Scans one target without following reparse points.
    /// </summary>
    public static async Task<ContentIntegrityScanResult> ScanAsync(
        ContentIntegrityTarget target,
        CancellationToken cancellationToken)
    {
        Dictionary<string, ContentIntegrityScannedFile> files = new(StringComparer.OrdinalIgnoreCase);
        List<string> emptyDirectories = [];
        List<string> unsafeLinks = [];
        List<ContentIntegrityScanError> errors = [];
        List<(string Path, string RelativePath)> fileCandidates = [];

        string root = LexicalPath.NormalizeFullPath(target.RootDirectory);
        if (!Directory.Exists(root))
        {
            return new ContentIntegrityScanResult(files, emptyDirectories, unsafeLinks, errors);
        }

        if (FileSystemPathSafety.IsReparsePoint(root))
        {
            unsafeLinks.Add(".");
            return new ContentIntegrityScanResult(files, emptyDirectories, unsafeLinks, errors);
        }

        Stack<string> pendingDirectories = new();
        pendingDirectories.Push(root);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pendingDirectories.Pop();
            string directoryRelativePath = ContentIntegrityPath.GetRelativePath(root, directory);

            try
            {
                bool isRootDirectory = LexicalPath.AreEquivalent(directory, root);
                if (!isRootDirectory &&
                    FileSystemPathSafety.IsReparsePoint(directory))
                {
                    unsafeLinks.Add(directoryRelativePath);
                    continue;
                }

                var entries = Directory.EnumerateFileSystemEntries(directory).ToList();
                if (entries.Count == 0 &&
                    !isRootDirectory &&
                    !ContentIntegrityPath.IsIgnored(target, directoryRelativePath))
                {
                    emptyDirectories.Add(directoryRelativePath);
                }

                foreach (string entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string relativePath = ContentIntegrityPath.GetRelativePath(root, entry);
                    FileAttributes attributes = File.GetAttributes(entry);
                    if (ContentIntegrityPath.IsIgnored(target, relativePath))
                    {
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            unsafeLinks.Add(relativePath);
                        }

                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        unsafeLinks.Add(relativePath);
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pendingDirectories.Push(entry);
                        continue;
                    }

                    fileCandidates.Add((entry, relativePath));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add(new ContentIntegrityScanError(directoryRelativePath, exception.Message));
            }
        }

        var scannedFiles = new ConcurrentDictionary<string, ContentIntegrityScannedFile>(
            StringComparer.OrdinalIgnoreCase);
        var scanErrors = new ConcurrentBag<ContentIntegrityScanError>();
        await Parallel.ForEachAsync(
            fileCandidates,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, MaxConcurrentFileHashes)
            },
            async (candidate, token) =>
            {
                try
                {
                    ContentIntegrityScannedFile scannedFile = await ScanFileAsync(
                            candidate.Path,
                            candidate.RelativePath,
                            token)
                        .ConfigureAwait(false);
                    scannedFiles[candidate.RelativePath] = scannedFile;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    scanErrors.Add(new ContentIntegrityScanError(candidate.RelativePath, exception.Message));
                }
            }).ConfigureAwait(false);

        foreach ((string relativePath, ContentIntegrityScannedFile scannedFile) in scannedFiles)
        {
            files[relativePath] = scannedFile;
        }

        errors.AddRange(scanErrors);

        return new ContentIntegrityScanResult(files, emptyDirectories, unsafeLinks, errors);
    }

    /// <summary>
    ///     Determines whether a completed scan exactly matches an expected safe file set.
    /// </summary>
    public static bool MatchesExpectedFileSet(
        ContentIntegrityScanResult scan,
        IReadOnlySet<string> expectedRelativePaths)
    {
        if (scan.EmptyDirectories.Count > 0 ||
            scan.UnsafeLinks.Count > 0 ||
            scan.Errors.Count > 0)
        {
            return false;
        }

        var normalizedExpectedPaths = expectedRelativePaths
            .Select(LexicalPath.NormalizeRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return normalizedExpectedPaths.SetEquals(scan.Files.Keys);
    }

    private static async Task<ContentIntegrityScannedFile> ScanFileAsync(
        string filePath,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        long length = stream.Length;
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return new ContentIntegrityScannedFile(relativePath, length, Convert.ToHexString(hash));
    }
}

internal sealed record ContentIntegrityScannedFile(string RelativePath, long Size, string Sha256);

internal sealed record ContentIntegrityScanError(string RelativePath, string Message);

internal sealed record ContentIntegrityScanResult(
    IReadOnlyDictionary<string, ContentIntegrityScannedFile> Files,
    IReadOnlyList<string> EmptyDirectories,
    IReadOnlyList<string> UnsafeLinks,
    IReadOnlyList<ContentIntegrityScanError> Errors);
