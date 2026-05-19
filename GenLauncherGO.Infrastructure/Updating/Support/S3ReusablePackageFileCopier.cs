using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Updating.Support;

/// <summary>
///     Copies unchanged files from the latest installed S3 package into a staging folder.
/// </summary>
internal sealed class S3ReusablePackageFileCopier
{
    private readonly IFileHashService _fileHashService;

    private readonly ILogger _logger;

    public S3ReusablePackageFileCopier(
        IFileHashService fileHashService,
        ILogger logger)
    {
        _fileHashService = fileHashService ?? throw new ArgumentNullException(nameof(fileHashService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task CopyUnchangedFilesAsync(
        OwnedContentPath sourcePath,
        OwnedContentPath destinationPath,
        IReadOnlyList<RemoteFileManifestEntry> repositoryFiles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(destinationPath);
        ArgumentNullException.ThrowIfNull(repositoryFiles);

        FileSystemPathSafety.ResolveOwnedSubpath(
            sourcePath.OwnerRoot,
            sourcePath.FullPath,
            "Reusable package paths",
            "launcher-owned content");
        FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(
            sourcePath.FullPath,
            "Reusable package paths");
        FileSystemPathSafety.ResolveOwnedSubpath(
            destinationPath.OwnerRoot,
            destinationPath.FullPath,
            "Package staging paths",
            "launcher-owned temporary storage");
        FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(
            destinationPath.FullPath,
            "Package staging paths");

        Dictionary<string, RemoteFileManifestEntry> repositoryFileIndex = BuildRepositoryFileIndex(repositoryFiles);
        await CopyReusableDirectoryContentAsync(
            sourcePath.FullPath,
            destinationPath.FullPath,
            repositoryFileIndex,
            string.Empty,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Builds a manifest lookup keyed by normalized relative paths and converted <c>.gib</c> aliases.
    /// </summary>
    private static Dictionary<string, RemoteFileManifestEntry> BuildRepositoryFileIndex(
        IReadOnlyList<RemoteFileManifestEntry> repositoryFiles)
    {
        Dictionary<string, RemoteFileManifestEntry> repositoryFileIndex =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (RemoteFileManifestEntry repositoryFile in repositoryFiles)
        {
            string normalizedPath = ManifestPathResolver.NormalizeForManifestIndex(repositoryFile.FileName);
            repositoryFileIndex[normalizedPath] = repositoryFile;

            string installedPath =
                ManifestPathResolver.NormalizeInstalledPathForManifestIndex(repositoryFile.FileName);
            if (!string.Equals(installedPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                repositoryFileIndex[installedPath] = repositoryFile;
            }
        }

        return repositoryFileIndex;
    }

    private async Task CopyReusableDirectoryContentAsync(
        string sourceDir,
        string destinationDir,
        Dictionary<string, RemoteFileManifestEntry> repositoryFileIndex,
        string pathAddition,
        CancellationToken cancellationToken)
    {
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            sourceDir,
            "Reusable package paths");
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            destinationDir,
            "Package staging paths");
        DirectoryInfo directory = new(sourceDir);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            _logger.LogWarning(
                "Skipped unsafe reusable package directory link {DirectoryName}.",
                directory.Name);
            return;
        }

        DirectoryInfo[] directories = directory.GetDirectories()
            .Where(subdirectory => (subdirectory.Attributes & FileAttributes.ReparsePoint) == 0)
            .ToArray();

        Directory.CreateDirectory(destinationDir);

        foreach (FileInfo file in directory.GetFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                _logger.LogWarning(
                    "Skipped unsafe reusable package file link {FileName}.",
                    file.Name);
                continue;
            }

            await CopyReusableFileAsync(
                file,
                destinationDir,
                repositoryFileIndex,
                pathAddition,
                cancellationToken).ConfigureAwait(false);
        }

        foreach (DirectoryInfo subDir in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await CopyReusableDirectoryContentAsync(
                subDir.FullName,
                Path.Combine(destinationDir, subDir.Name),
                repositoryFileIndex,
                ManifestPathResolver.NormalizeForManifestIndex(Path.Combine(pathAddition, subDir.Name)),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CopyReusableFileAsync(
        FileInfo file,
        string destinationDir,
        Dictionary<string, RemoteFileManifestEntry> repositoryFileIndex,
        string pathAddition,
        CancellationToken cancellationToken)
    {
        string targetFilePath = ManifestPathResolver.ResolvePath(destinationDir, file.Name);
        string relativeFilePath = ManifestPathResolver.NormalizeForManifestIndex(
            Path.Combine(pathAddition, file.Name));

        RemoteFileManifestEntry? repositoryFile = repositoryFileIndex.GetValueOrDefault(relativeFilePath);
        if (File.Exists(targetFilePath) || repositoryFile is null)
        {
            return;
        }

        if (!S3HashValidationPolicy.IsReliableMd5Hash(repositoryFile.Hash))
        {
            return;
        }

        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            file.FullName,
            "Reusable package paths");
        // Keep the source handle open so Windows denies writes and replacement until the verified bytes are copied.
        await using FileStream sourceStream = file.OpenRead();
        if (repositoryFile.Size != (ulong)sourceStream.Length)
        {
            return;
        }

        string hash = await _fileHashService
            .ComputeMd5HashAsync(file.FullName, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(hash, repositoryFile.Hash, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            file.FullName,
            "Reusable package paths");
        FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            destinationDir,
            "Package staging paths");
        await using FileStream destinationStream = new(
            targetFilePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
    }
}
