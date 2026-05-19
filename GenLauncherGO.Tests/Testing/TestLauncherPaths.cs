using System;
using System.IO;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Tests.Testing;

internal static class TestLauncherPaths
{
    private const string DefaultGameDirectory = @"C:\Games\ZeroHour";

    public static LauncherPaths Create(
        string gameDirectory = DefaultGameDirectory,
        SupportedGame game = SupportedGame.ZeroHour)
    {
        string fullGameDirectory = Path.GetFullPath(gameDirectory);
        string gameParentDirectory = Path.GetDirectoryName(fullGameDirectory)
            ?? throw new ArgumentException("The test game directory must have a parent.", nameof(gameDirectory));
        string executableDirectory = Path.Combine(
            gameParentDirectory,
            Path.GetFileName(fullGameDirectory) + "-Launcher");
        return new LauncherStoragePaths(executableDirectory).CreateGamePaths(game, gameDirectory);
    }

    public static LauncherPaths Create(TestDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        string gameDirectory = directory.CreateDirectory("Game");
        string executableDirectory = directory.CreateDirectory("Launcher");
        LauncherPaths paths = new LauncherStoragePaths(executableDirectory)
            .CreateGamePaths(SupportedGame.ZeroHour, gameDirectory);
        Directory.CreateDirectory(paths.ImagesDirectory);
        Directory.CreateDirectory(paths.ModsDirectory);
        Directory.CreateDirectory(paths.TempDirectory);
        Directory.CreateDirectory(paths.DeploymentDirectory);
        return paths;
    }

    public static LauncherRuntimePathContext CreateRuntimePathContext(LauncherPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        string dataDirectory = Path.GetDirectoryName(paths.OwnedGameDataDirectory)
            ?? throw new ArgumentException("The owned game data directory must have a parent.", nameof(paths));
        string executableDirectory = Path.GetDirectoryName(dataDirectory)
            ?? throw new ArgumentException("The shared launcher data directory must have a parent.", nameof(paths));
        return new LauncherRuntimePathContext(new LauncherStoragePaths(executableDirectory), paths);
    }
}
