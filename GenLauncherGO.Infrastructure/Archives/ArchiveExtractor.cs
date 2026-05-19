using System;
using System.IO;
using System.Threading;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Infrastructure.Archives.Contracts;
using GenLauncherGO.Infrastructure.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace GenLauncherGO.Infrastructure.Archives;

/// <summary>
///     Extracts archive files using the configured infrastructure archive library, creating destination directories,
///     overwriting extracted files, and optionally renaming extracted <c>.big</c> entries to <c>.gib</c>.
/// </summary>
internal sealed class ArchiveExtractor : IArchiveExtractor
{
    private readonly ILogger<ArchiveExtractor> _logger;

    public ArchiveExtractor(ILogger<ArchiveExtractor>? logger = null)
    {
        _logger = logger ?? NullLogger<ArchiveExtractor>.Instance;
    }

    public void ExtractToDirectory(
        string archiveFilePath,
        string destinationDirectory,
        bool convertBigFilesToGib = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        string destinationRoot = LexicalPath.NormalizeFullPath(destinationDirectory);
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            destinationRoot,
            "Archive extraction destinations");
        Directory.CreateDirectory(destinationRoot);
        FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(
            destinationRoot,
            "Archive extraction destinations");

        _logger.LogInformation(
            "Extracting archive {ArchiveFilePath} to {DestinationDirectory}. Convert .big files to .gib: {ConvertBigFilesToGib}",
            Path.GetFileName(archiveFilePath),
            Path.GetFileName(destinationRoot),
            convertBigFilesToGib);

        using FileStream archiveStream = File.OpenRead(archiveFilePath);
        using IArchive archive = ArchiveFactory.OpenArchive(
            archiveStream,
            new ReaderOptions { LeaveStreamOpen = false });

        int extractedEntryCount = 0;
        foreach (IArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.IsDirectory)
            {
                continue;
            }

            string entryPath = GetDestinationPath(destinationRoot, entry.Key, convertBigFilesToGib);
            string? entryDirectory = Path.GetDirectoryName(entryPath);
            if (!string.IsNullOrWhiteSpace(entryDirectory))
            {
                FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
                    entryDirectory,
                    "Archive entry paths");
                Directory.CreateDirectory(entryDirectory);
                FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
                    entryDirectory,
                    "Archive entry paths");
            }

            FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
                entryPath,
                "Archive entry paths");
            entry.WriteToFile(entryPath, new ExtractionOptions
            {
                Overwrite = true,
                PreserveFileTime = true
            });

            extractedEntryCount++;
        }

        _logger.LogInformation(
            "Extracted {EntryCount} archive entries to {DestinationDirectory}",
            extractedEntryCount,
            Path.GetFileName(destinationRoot));
    }

    /// <summary>
    ///     Resolves the destination path for an archive entry and rejects paths outside the extraction root.
    /// </summary>
    /// <exception cref="InvalidDataException">
    ///     Thrown when the archive entry has no usable file name or would extract outside the destination directory.
    /// </exception>
    private static string GetDestinationPath(
        string destinationRoot,
        string? entryKey,
        bool convertBigFilesToGib)
    {
        if (string.IsNullOrWhiteSpace(entryKey))
        {
            throw new InvalidDataException("Archive entry is missing a file name.");
        }

        string normalizedEntryKey = entryKey.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        string containmentFailureMessage =
            $"Archive entry '{entryKey}' would extract outside the destination folder.";
        string destinationPath = LexicalPath.ResolveContainedPath(
            destinationRoot,
            normalizedEntryKey,
            containmentFailureMessage);
        if (convertBigFilesToGib)
        {
            destinationPath = BigFileVariantPath.GetInstalledPath(destinationPath);
            destinationPath = LexicalPath.ResolveContainedPath(
                destinationRoot,
                destinationPath,
                containmentFailureMessage);
        }

        return destinationPath;
    }
}
