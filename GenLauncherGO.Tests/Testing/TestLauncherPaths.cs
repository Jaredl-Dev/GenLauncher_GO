using System;
using System.IO;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Builds the launcher path sets tests need through the production <see cref="LauncherStoragePaths" /> authority,
///     so no test restates the owned folder layout.
/// </summary>
internal static class TestLauncherPaths
{
    private const string DefaultGameDirectory = @"C:\Games\ZeroHour";

    private const string VirtualRootFolderName = "GenLauncherGO.Tests";

    public static LauncherPaths Create(
        string gameDirectory = DefaultGameDirectory,
        SupportedGame game = SupportedGame.ZeroHour)
    {
        string fullGameDirectory = Path.GetFullPath(gameDirectory);
        string gameParentDirectory = Path.GetDirectoryName(fullGameDirectory)
                                     ?? throw new ArgumentException("The test game directory must have a parent.",
                                         nameof(gameDirectory));
        string executableDirectory = Path.Combine(
            gameParentDirectory,
            Path.GetFileName(fullGameDirectory) + "-Launcher");
        return new LauncherStoragePaths(executableDirectory).CreateGamePaths(game, gameDirectory);
    }

    public static LauncherPaths Create(TestDirectory directory, SupportedGame game = SupportedGame.ZeroHour)
    {
        ArgumentNullException.ThrowIfNull(directory);

        string gameDirectory = directory.CreateDirectory("Game");
        string executableDirectory = directory.CreateDirectory("Launcher");
        return CreateOwnedDirectories(
            new LauncherStoragePaths(executableDirectory).CreateGamePaths(game, gameDirectory));
    }

    /// <summary>
    ///     Builds a path set below the test working directory without creating anything on disk, for tests that only
    ///     assert how paths are composed.
    /// </summary>
    public static LauncherPaths CreateVirtualRoot(string name, SupportedGame game = SupportedGame.ZeroHour)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string root = Path.GetFullPath(Path.Combine(VirtualRootFolderName, name));
        return new LauncherStoragePaths(Path.Combine(root, "Launcher"))
            .CreateGamePaths(game, Path.Combine(root, "Game"));
    }

    /// <summary>
    ///     Builds both supported games from one storage root, which is the only arrangement a game switch is valid in.
    /// </summary>
    public static (LauncherRuntimePathContext RuntimePaths, LauncherPaths Generals, LauncherPaths ZeroHour)
        CreateTwoGameRuntime(TestDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        var storagePaths = new LauncherStoragePaths(directory.CreateDirectory("Launcher"));
        LauncherPaths generalsPaths = CreateOwnedDirectories(storagePaths.CreateGamePaths(
            SupportedGame.Generals,
            directory.CreateDirectory("Generals")));
        LauncherPaths zeroHourPaths = CreateOwnedDirectories(storagePaths.CreateGamePaths(
            SupportedGame.ZeroHour,
            directory.CreateDirectory("ZeroHour")));

        return (new LauncherRuntimePathContext(storagePaths, zeroHourPaths), generalsPaths, zeroHourPaths);
    }

    public static LauncherRuntimePathContext CreateRuntimePathContext(LauncherPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        string dataDirectory = Path.GetDirectoryName(paths.OwnedGameDataDirectory)
                               ?? throw new ArgumentException("The owned game data directory must have a parent.",
                                   nameof(paths));
        string executableDirectory = Path.GetDirectoryName(dataDirectory)
                                     ?? throw new ArgumentException(
                                         "The shared launcher data directory must have a parent.", nameof(paths));
        return new LauncherRuntimePathContext(new LauncherStoragePaths(executableDirectory), paths);
    }

    private static LauncherPaths CreateOwnedDirectories(LauncherPaths paths)
    {
        Directory.CreateDirectory(paths.OwnedGameDataDirectory);
        Directory.CreateDirectory(paths.ImagesDirectory);
        Directory.CreateDirectory(paths.ModsDirectory);
        Directory.CreateDirectory(paths.TempDirectory);
        Directory.CreateDirectory(paths.DeploymentDirectory);
        Directory.CreateDirectory(paths.StateDirectory);
        return paths;
    }
}
