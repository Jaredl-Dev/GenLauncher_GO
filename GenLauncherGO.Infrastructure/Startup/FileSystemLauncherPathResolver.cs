using System;
using System.IO;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Infrastructure.Startup;

/// <summary>
///     Resolves and safely prepares standalone launcher-owned paths.
/// </summary>
public sealed class FileSystemLauncherPathResolver : ILauncherPathResolver
{
    private readonly ILogger<FileSystemLauncherPathResolver> _logger;

    public FileSystemLauncherPathResolver()
        : this(NullLogger<FileSystemLauncherPathResolver>.Instance)
    {
    }

    public FileSystemLauncherPathResolver(ILogger<FileSystemLauncherPathResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public LauncherStoragePaths Resolve(string executableDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);

        return new LauncherStoragePaths(executableDirectory);
    }

    public void PrepareLauncherDirectories(LauncherStoragePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        OwnedDirectoryTree.EnsureExists(paths.ExecutableDirectory, paths.DataDirectory);
        OwnedDirectoryTree.EnsureExists(paths.DataDirectory, paths.LogsDirectory);

        _logger.LogInformation("Prepared shared standalone launcher directories.");
    }

    public void PrepareGameDirectories(LauncherPaths paths, bool cleanTemporaryDirectory)
    {
        ArgumentNullException.ThrowIfNull(paths);

        string dataDirectory = Path.GetDirectoryName(paths.OwnedGameDataDirectory)
                               ?? throw new InvalidDataException(
                                   "A per-game data directory must have an owning shared data directory.");
        OwnedDirectoryTree.EnsureExists(dataDirectory, paths.OwnedGameDataDirectory);
        OwnedDirectoryTree.EnsureExists(paths.OwnedGameDataDirectory, paths.RuntimeDirectory);
        OwnedDirectoryTree.EnsureExists(paths.OwnedGameDataDirectory, paths.CacheDirectory);
        OwnedDirectoryTree.EnsureExists(paths.OwnedGameDataDirectory, paths.ImagesDirectory);
        OwnedDirectoryTree.EnsureExists(paths.OwnedGameDataDirectory, paths.ModsDirectory);
        OwnedDirectoryTree.EnsureExists(paths.OwnedGameDataDirectory, paths.TempDirectory);
        OwnedDirectoryTree.EnsureExists(paths.OwnedGameDataDirectory, paths.DeploymentDirectory);
        OwnedDirectoryTree.EnsureExists(paths.OwnedGameDataDirectory, paths.IntegrityDirectory);
        OwnedDirectoryTree.EnsureExists(paths.OwnedGameDataDirectory, paths.StateDirectory);

        if (cleanTemporaryDirectory)
        {
            // Staged packages are kept: a download the launcher suspended on close leaves its partial content
            // here, and that content is exactly what lets the next session resume instead of starting over.
            OwnedDirectoryTree.PrepareEmptyExcept(
                paths.OwnedGameDataDirectory,
                paths.TempDirectory,
                paths.PackagesDirectory);
        }

        _logger.LogInformation(
            "Prepared isolated launcher directories for {SupportedGame}.",
            paths.Game);
    }
}
