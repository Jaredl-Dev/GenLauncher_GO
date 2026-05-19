using System;
using System.Threading;
using GenLauncherGO.Core.IO;

namespace GenLauncherGO.Core.Startup;

/// <summary>
///     Owns the active immutable game-path snapshot for a running standalone launcher session.
/// </summary>
/// <remarks>
///     Long-running operations must read <see cref="ActivePaths" /> once and retain that snapshot for their full
///     lifetime. Replacing the active paths never redirects an operation that is already in progress.
/// </remarks>
public sealed class LauncherRuntimePathContext
{
    private LauncherPaths _activePaths;

    public LauncherRuntimePathContext(
        LauncherStoragePaths storagePaths,
        LauncherPaths initialPaths)
    {
        StoragePaths = storagePaths ?? throw new ArgumentNullException(nameof(storagePaths));
        _activePaths = ValidateOwnedGamePaths(initialPaths);
    }

    public LauncherStoragePaths StoragePaths { get; }

    public LauncherPaths ActivePaths => Volatile.Read(ref _activePaths);

    /// <summary>
    ///     Atomically replaces the active game-path snapshot.
    /// </summary>
    public void SwitchActive(LauncherPaths newPaths)
    {
        Interlocked.Exchange(ref _activePaths, ValidateOwnedGamePaths(newPaths));
    }

    private LauncherPaths ValidateOwnedGamePaths(LauncherPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        string expectedDataDirectory = StoragePaths.GetGameDataDirectory(paths.Game);
        if (!LexicalPath.AreEquivalent(paths.OwnedGameDataDirectory, expectedDataDirectory))
        {
            throw new ArgumentException(
                "The active path set must use the standalone launcher's canonical per-game data directory.",
                nameof(paths));
        }

        return paths;
    }
}
