using System;
using System.IO;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Mods.Models;

namespace GenLauncherGO.Core.Startup;

/// <summary>
///     Describes one supported game installation and its isolated launcher-owned data directory.
/// </summary>
public sealed record LauncherPaths
{
    private const string LauncherDataFileName = "LauncherData.yaml";

    private const string OwnedPathContainmentFailureMessage =
        "The resolved path must stay inside the launcher-owned directory.";

    internal LauncherPaths(SupportedGame game, string gameDirectory, string ownedGameDataDirectory)
    {
        PerGame.EnsureSupported(game, nameof(game));

        Game = game;
        GameDirectory = LexicalPath.NormalizeFullPath(gameDirectory);
        OwnedGameDataDirectory = LexicalPath.NormalizeFullPath(ownedGameDataDirectory);
    }

    public SupportedGame Game { get; }

    public string GameDirectory { get; }

    public string OwnedGameDataDirectory { get; }

    public string RuntimeDirectory => Path.Combine(OwnedGameDataDirectory, LauncherFileSystemLayout.RuntimeFolderName);

    public string CacheDirectory => Path.Combine(RuntimeDirectory, LauncherFileSystemLayout.CacheFolderName);

    public string ImagesDirectory => Path.Combine(CacheDirectory, LauncherFileSystemLayout.ImagesFolderName);

    public string ModsDirectory => Path.Combine(OwnedGameDataDirectory, LauncherFileSystemLayout.ModsFolderName);

    public string TempDirectory => Path.Combine(RuntimeDirectory, LauncherFileSystemLayout.TempFolderName);

    public string DeploymentDirectory => Path.Combine(RuntimeDirectory, LauncherFileSystemLayout.DeploymentFolderName);

    public string StateDirectory => Path.Combine(RuntimeDirectory, LauncherFileSystemLayout.StateFolderName);

    public string IntegrityDirectory => Path.Combine(RuntimeDirectory, LauncherFileSystemLayout.IntegrityFolderName);

    public string PackagesDirectory => Path.Combine(TempDirectory, LauncherFileSystemLayout.PackagesFolderName);

    public string PackageBackupsDirectory =>
        Path.Combine(StateDirectory, LauncherFileSystemLayout.PackageBackupsFolderName);

    public string LauncherDataFilePath => Path.Combine(StateDirectory, LauncherDataFileName);

    /// <summary>
    ///     Builds the image cache directory path for a mod, add-on, patch, or advertisement entry.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="modificationName" /> is empty, whitespace, or unsafe for a path segment.
    /// </exception>
    public string GetModificationImagesDirectory(string modificationName)
    {
        string safeModificationName = LexicalPath.NormalizePathSegment(modificationName, nameof(modificationName));

        return ResolveOwnedPath(ImagesDirectory, safeModificationName);
    }

    /// <summary>
    ///     Builds an image cache file path for a mod, add-on, patch, or advertisement entry.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="modificationName" /> or <paramref name="imageFileName" /> is empty, whitespace, or
    ///     unsafe for a path segment.
    /// </exception>
    public string GetModificationImageFilePath(string modificationName, string imageFileName)
    {
        string safeImageFileName = LexicalPath.NormalizePathSegment(imageFileName, nameof(imageFileName));

        return ResolveOwnedPath(GetModificationImagesDirectory(modificationName), safeImageFileName);
    }

    /// <summary>
    ///     Builds a package staging folder path below the launcher temporary directory.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="installedFolderPath" /> is empty, whitespace, or cannot be staged below the
    ///     launcher temporary package directory.
    /// </exception>
    private string GetPackageTemporaryFolderPath(string installedFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installedFolderPath);

        string installedFullPath = LexicalPath.NormalizeFullPath(installedFolderPath);
        string relativePath = LexicalPath.GetRelativePath(ModsDirectory, installedFullPath);

        if (LexicalPath.RelativePathLeavesRoot(relativePath))
        {
            relativePath = LexicalPath.NormalizePathSegment(
                Path.GetFileName(installedFullPath),
                nameof(installedFolderPath));
        }

        string temporaryPackagesRoot = PackagesDirectory;
        return ResolveOwnedPath(
            temporaryPackagesRoot,
            relativePath,
            "The installed package path cannot be staged outside the launcher temporary package folder.",
            nameof(installedFolderPath));
    }

    /// <summary>
    ///     Builds an owned package staging path for an installed content path.
    /// </summary>
    public OwnedContentPath GetPackageTemporaryPath(OwnedContentPath installedPath)
    {
        ArgumentNullException.ThrowIfNull(installedPath);

        string temporaryFolderPath = GetPackageTemporaryFolderPath(installedPath.FullPath);
        return new OwnedContentPath(PackagesDirectory, temporaryFolderPath);
    }

    /// <summary>
    ///     Builds a durable recovery-backup path that mirrors an installed package below the canonical Mods directory.
    /// </summary>
    /// <remarks>
    ///     Recovery backups live below State rather than Temp so startup cleanup cannot erase an interrupted replacement.
    /// </remarks>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="installedPath" /> is not below the canonical Mods directory.
    /// </exception>
    public OwnedContentPath GetPackageBackupPath(OwnedContentPath installedPath)
    {
        ArgumentNullException.ThrowIfNull(installedPath);

        if (!LexicalPath.IsPathBelowDirectory(installedPath.FullPath, ModsDirectory))
        {
            throw new ArgumentException(
                "Package recovery backups can only be created for content below the launcher Mods directory.",
                nameof(installedPath));
        }

        string relativePath = LexicalPath.GetRelativePath(ModsDirectory, installedPath.FullPath);
        string backupPath = ResolveOwnedPath(PackageBackupsDirectory, relativePath);
        return new OwnedContentPath(PackageBackupsDirectory, backupPath);
    }

    private static string ResolveOwnedPath(
        string rootDirectory,
        string relativePath,
        string containmentFailureMessage = OwnedPathContainmentFailureMessage,
        string? parameterName = null)
    {
        try
        {
            return LexicalPath.ResolveContainedPath(rootDirectory, relativePath, containmentFailureMessage);
        }
        catch (InvalidDataException)
        {
            throw new ArgumentException(containmentFailureMessage, parameterName);
        }
    }
}
