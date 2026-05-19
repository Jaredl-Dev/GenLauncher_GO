using System;
using System.IO;
using GenLauncherGO.Core.IO;

namespace GenLauncherGO.Core.Startup;

/// <summary>
///     Defines the standalone launcher's shared storage root and isolated per-title data roots.
/// </summary>
/// <remarks>
///     The executable directory is chosen by the user. All durable launcher state stays below
///     <see cref="DataDirectory" /> and never uses a game installation as an ownership boundary.
/// </remarks>
public sealed record LauncherStoragePaths
{
    private const string PreferencesFileName = "LauncherPreferences.yaml";

    public LauncherStoragePaths(string executableDirectory)
    {
        ExecutableDirectory = LexicalPath.NormalizeFullPath(executableDirectory);
    }

    public string ExecutableDirectory { get; }

    public string DataDirectory =>
        Path.Combine(ExecutableDirectory, LauncherFileSystemLayout.LauncherDataFolderName);

    public string LogsDirectory => Path.Combine(DataDirectory, LauncherFileSystemLayout.LogsFolderName);

    public string PreferencesFilePath => Path.Combine(DataDirectory, PreferencesFileName);

    /// <summary>
    ///     Gets the launcher-owned data root isolated to one supported game.
    /// </summary>
    internal string GetGameDataDirectory(SupportedGame game)
    {
        return Path.Combine(DataDirectory, LauncherFileSystemLayout.GetGameDataFolderName(game));
    }

    /// <summary>
    ///     Creates the sole canonical path set for a validated game installation.
    /// </summary>
    public LauncherPaths CreateGamePaths(SupportedGame game, string validatedGameRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(validatedGameRoot);

        return new LauncherPaths(game, validatedGameRoot, GetGameDataDirectory(game));
    }
}
