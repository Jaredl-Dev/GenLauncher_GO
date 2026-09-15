using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using GenLauncherGO.Shared.Archives;
using GenLauncherGO.Shared.IO;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Features.Mods;

/// <summary>
///     Imports manually selected modification content by copying files and folders, extracting supported archives, and
///     converting <c>.big</c> packages to launcher-managed <c>.gib</c> files.
/// </summary>
internal sealed class FileSystemManualModificationImporter : IManualModificationImporter
{
    /// <summary>
    ///     The number of nested single-folder wrappers unwrapped before the import is left as it arrived.
    /// </summary>
    private const int MaxWrapperDirectoryDepth = 4;

    /// <summary>
    ///     Folder names the game itself reads from a content root.
    /// </summary>
    /// <remarks>
    ///     An INI-only modification is often nothing but a <c>Data</c> folder. Unwrapping that would hoist its children
    ///     to the content root and destroy the very structure the game looks for, so a lone folder named here is
    ///     content rather than packaging.
    /// </remarks>
    private static readonly HashSet<string> _gameContentDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Data",
        "Art",
        "Maps",
        "Movies",
        "Music",
        "Window",
        "Lang",
        "Scripts",
        "Textures"
    };

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
        IReadOnlyList<string> sourcePaths,
        OwnedContentPath destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        ArgumentNullException.ThrowIfNull(destinationPath);

        if (sourcePaths.Count == 0)
        {
            throw new ArgumentException("At least one source file is required.", nameof(sourcePaths));
        }

        string destinationDirectory = destinationPath.FullPath;
        try
        {
            foreach (string sourcePath in sourcePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                destinationDirectory = PrepareSafeDestination(destinationPath);
                if (Directory.Exists(sourcePath))
                {
                    ImportDirectory(sourcePath, destinationDirectory, cancellationToken);
                    continue;
                }

                ImportFile(
                    sourcePath,
                    destinationPath,
                    destinationDirectory,
                    cancellationToken);
            }

            UnwrapRedundantDirectories(destinationPath);

            _logger.LogInformation(
                "Imported {SourceCount} manual content source(s) to {DestinationDirectory}.",
                sourcePaths.Count,
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

        if (!LauncherContentFileTypes.IsArchive(sourceFileName))
        {
            CopyImportedFile(sourceFilePath, sourceFileName, destinationDirectory);
            return;
        }

        string archiveFilePath = ResolveSafeDestinationFilePath(
            destinationDirectory,
            Path.Combine(destinationDirectory, sourceFileName));
        if (!File.Exists(archiveFilePath))
        {
            File.Copy(sourceFilePath, archiveFilePath);
        }

        destinationDirectory = PrepareSafeDestination(destinationPath);
        archiveFilePath = ResolveSafeDestinationFilePath(destinationDirectory, archiveFilePath);

        // Archives carry the same packages a download does, so their .big entries are stored under the inert .gib
        // name here for the same reason: an imported archive and a downloaded one must leave the same folder behind.
        _archiveExtractor.ExtractToDirectory(
            archiveFilePath,
            destinationDirectory,
            true,
            cancellationToken);
        destinationDirectory = PrepareSafeDestination(destinationPath);
        ResolveSafeDestinationFilePath(destinationDirectory, archiveFilePath);
        File.Delete(archiveFilePath);
    }

    /// <summary>
    ///     Copies the contents of a selected folder into the destination, preserving its subfolder structure.
    /// </summary>
    /// <remarks>
    ///     The folder itself is not recreated: a user selecting a folder is naming the content root, not asking for
    ///     another level around it. Archives inside a selected folder are copied rather than extracted, because the
    ///     folder is imported as the author arranged it.
    /// </remarks>
    private void ImportDirectory(
        string sourceDirectory,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        if (FileSystemPathSafety.IsReparsePoint(sourceDirectory))
        {
            _logger.LogWarning(
                "Skipped manual import source {DirectoryName} because it is a link.",
                Path.GetFileName(sourceDirectory));
            return;
        }

        var directory = new DirectoryInfo(sourceDirectory);
        foreach (FileInfo file in directory.GetFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                _logger.LogWarning("Skipped linked manual import file {FileName}.", file.Name);
                continue;
            }

            CopyImportedFile(file.FullName, file.Name, destinationDirectory);
        }

        foreach (DirectoryInfo childDirectory in directory.GetDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((childDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                _logger.LogWarning("Skipped linked manual import folder {DirectoryName}.", childDirectory.Name);
                continue;
            }

            string childDestination = ResolveSafeDestinationFilePath(
                destinationDirectory,
                Path.Combine(destinationDirectory, childDirectory.Name));
            FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
                childDestination,
                "Manual import destinations");
            Directory.CreateDirectory(childDestination);
            ImportDirectory(childDirectory.FullName, childDestination, cancellationToken);
        }
    }

    /// <summary>
    ///     Copies one imported file under its installed name, leaving content that is already in place untouched.
    /// </summary>
    private static void CopyImportedFile(
        string sourceFilePath,
        string fileName,
        string destinationDirectory)
    {
        string destinationFilePath = ResolveSafeDestinationFilePath(
            destinationDirectory,
            Path.Combine(destinationDirectory, fileName));
        string installedFilePath = BigFileVariantPath.GetInstalledPath(destinationFilePath);
        if (!LexicalPath.AreEquivalent(installedFilePath, destinationFilePath))
        {
            installedFilePath = ResolveSafeDestinationFilePath(destinationDirectory, installedFilePath);
        }

        if (!File.Exists(installedFilePath))
        {
            File.Copy(sourceFilePath, installedFilePath);
        }
    }

    /// <summary>
    ///     Removes packaging folders that wrap the imported content, so an archive built around its own folder does
    ///     not install one level below where the game reads it.
    /// </summary>
    /// <remarks>
    ///     A lone folder is only ever packaging when nothing sits beside it. Any second entry at the content root
    ///     means the author placed that folder deliberately, and it is left alone.
    /// </remarks>
    private void UnwrapRedundantDirectories(OwnedContentPath destinationPath)
    {
        for (int depth = 0; depth < MaxWrapperDirectoryDepth; depth++)
        {
            string destinationDirectory = PrepareSafeDestination(destinationPath);
            string[] entries = Directory.GetFileSystemEntries(destinationDirectory);
            if (entries.Length != 1)
            {
                return;
            }

            string wrapperDirectory = entries[0];
            string wrapperName = Path.GetFileName(wrapperDirectory);
            if (!Directory.Exists(wrapperDirectory) ||
                FileSystemPathSafety.IsReparsePoint(wrapperDirectory) ||
                _gameContentDirectoryNames.Contains(wrapperName))
            {
                return;
            }

            foreach (string childEntry in Directory.GetFileSystemEntries(wrapperDirectory))
            {
                string movedEntry = ResolveSafeDestinationFilePath(
                    destinationDirectory,
                    Path.Combine(destinationDirectory, Path.GetFileName(childEntry)));
                if (Directory.Exists(childEntry))
                {
                    Directory.Move(childEntry, movedEntry);
                    continue;
                }

                File.Move(childEntry, movedEntry);
            }

            Directory.Delete(wrapperDirectory);
            _logger.LogInformation(
                "Unwrapped manual import packaging folder {DirectoryName}.",
                wrapperName);
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
