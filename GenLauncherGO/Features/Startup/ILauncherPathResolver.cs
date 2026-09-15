namespace GenLauncherGO.Features.Startup;

/// <summary>
///     Resolves and prepares the launcher-owned directories for a GenLauncherGO session.
/// </summary>
internal interface ILauncherPathResolver
{
    /// <summary>
    ///     Resolves launcher paths from the executable directory.
    /// </summary>
    LauncherStoragePaths Resolve(string executableDirectory);

    /// <summary>
    ///     Creates the shared launcher-owned directories.
    /// </summary>
    void PrepareLauncherDirectories(LauncherStoragePaths paths);

    /// <summary>
    ///     Creates launcher-owned directories for one supported game and optionally clears its temporary files.
    /// </summary>
    void PrepareGameDirectories(LauncherPaths paths, bool cleanTemporaryDirectory);
}
