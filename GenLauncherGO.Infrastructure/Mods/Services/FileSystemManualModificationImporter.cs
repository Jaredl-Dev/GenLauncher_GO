using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Archives.Contracts;
using GenLauncherGO.Infrastructure.Common;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Mods.Services;

/// <summary>
///     Imports manually selected modification files by copying files, extracting supported archives, and converting loose
///     <c>.big</c> packages to launcher-managed <c>.gib</c> files.
/// </summary>
internal sealed class FileSystemManualModificationImporter : IManualModificationImporter
{
    private readonly IArchiveExtractor _archiveExtractor;

    private readonly ILogger<FileSystemManualModificationImporter> _logger;

    public FileSystemManualModificationImporter(
        IArchiveExtractor archiveExtractor,
        ILogger<FileSystemManualModificationImporter> logger)
    {
        _archiveExtractor = archiveExtractor ?? throw new ArgumentNullException(nameof(archiveExtractor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Import(
        IReadOnlyList<string> sourceFilePaths,
        OwnedContentPath destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePaths);
        ArgumentNullException.ThrowIfNull(destinationPath);

        if (sourceFilePaths.Count == 0)
        {
            throw new ArgumentException("At least one source file is required.", nameof(sourceFilePaths));
        }

        string destinationDirectory = destinationPath.FullPath;
        try
        {
            foreach (string sourceFilePath in sourceFilePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                destinationDirectory = PrepareSafeDestination(destinationPath);
                ImportFile(
                    sourceFilePath,
                    destinationPath,
                    destinationDirectory,
                    cancellationToken);
            }

            _logger.LogInformation(
                "Imported {FileCount} manual content file(s) to {DestinationDirectory}.",
                sourceFilePaths.Count,
                Path.GetFileName(destinationDirectory));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Failed to import manual content into {DestinationDirectory}.",
                Path.GetFileName(destinationDirectory));
            throw;
        }
    }

    private void ImportFile(
        string sourceFilePath,
        OwnedContentPath destinationPath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        string sourceFileName = Path.GetFileName(sourceFilePath);
        if (string.IsNullOrWhiteSpace(sourceFileName))
        {
            throw new ArgumentException("Source file path must include a file name.", nameof(sourceFilePath));
        }

        string destinationFilePath = ResolveSafeDestinationFilePath(
            destinationDirectory,
            Path.Combine(destinationDirectory, sourceFileName));
        if (!File.Exists(destinationFilePath))
        {
            File.Copy(sourceFilePath, destinationFilePath);
        }

        if (LauncherContentFileTypes.IsArchive(sourceFileName))
        {
            destinationDirectory = PrepareSafeDestination(destinationPath);
            destinationFilePath = ResolveSafeDestinationFilePath(
                destinationDirectory,
                destinationFilePath);
            _archiveExtractor.ExtractToDirectory(
                destinationFilePath,
                destinationDirectory,
                cancellationToken: cancellationToken);
            destinationDirectory = PrepareSafeDestination(destinationPath);
            ResolveSafeDestinationFilePath(destinationDirectory, destinationFilePath);
            File.Delete(destinationFilePath);
            return;
        }

        string installedFilePath = BigFileVariantPath.GetInstalledPath(destinationFilePath);
        if (!LexicalPath.AreEquivalent(installedFilePath, destinationFilePath))
        {
            string gibFilePath = ResolveSafeDestinationFilePath(
                destinationDirectory,
                installedFilePath);
            File.Move(destinationFilePath, gibFilePath);
        }
    }

    /// <summary>
    ///     Creates the owned destination when needed and rejects any linked path before mutation or extraction.
    /// </summary>
    private static string PrepareSafeDestination(OwnedContentPath destinationPath)
    {
        string destinationDirectory = FileSystemPathSafety.ResolveOwnedSubpath(
            destinationPath.OwnerRoot,
            destinationPath.FullPath,
            "Manual import destinations",
            "their launcher-owned root");
        destinationDirectory = OwnedDirectoryTree.EnsureExists(
            destinationPath.OwnerRoot,
            destinationDirectory);
        FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(
            destinationDirectory,
            "Manual import destinations");
        return destinationDirectory;
    }

    /// <summary>
    ///     Resolves one destination file and rejects paths or existing entries outside the safe import directory.
    /// </summary>
    private static string ResolveSafeDestinationFilePath(
        string destinationDirectory,
        string candidatePath)
    {
        return FileSystemPathSafety.ResolveOwnedSubpath(
            destinationDirectory,
            candidatePath,
            "Manual import files",
            "their destination directory");
    }
}
