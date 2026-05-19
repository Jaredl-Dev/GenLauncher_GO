using System;
using System.IO;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Tests.Core.Startup;

public sealed class LauncherStoragePathsTests
{
    [Fact]
    public void ConstructorDerivesSharedStandaloneAndIsolatedGamePaths()
    {
        string executableDirectory = Path.GetFullPath(Path.Combine("GenLauncherGO.Tests", "Launcher"));
        string gameDirectory = Path.GetFullPath(Path.Combine("GenLauncherGO.Tests", "ZeroHour"));
        var storage = new LauncherStoragePaths(executableDirectory);

        LauncherPaths gamePaths = storage.CreateGamePaths(SupportedGame.ZeroHour, gameDirectory);

        storage.ExecutableDirectory.Should().Be(executableDirectory);
        storage.DataDirectory.Should().Be(Path.Combine(executableDirectory, "GenLauncherGO Data"));
        storage.LogsDirectory.Should().Be(Path.Combine(executableDirectory, "GenLauncherGO Data", "Logs"));
        storage.PreferencesFilePath.Should().Be(
            Path.Combine(executableDirectory, "GenLauncherGO Data", "LauncherPreferences.yaml"));
        gamePaths.Game.Should().Be(SupportedGame.ZeroHour);
        gamePaths.GameDirectory.Should().Be(gameDirectory);
        gamePaths.OwnedGameDataDirectory.Should().Be(
            Path.Combine(executableDirectory, "GenLauncherGO Data", "C&C Zero Hour Data"));
        gamePaths.ModsDirectory.Should().Be(
            Path.Combine(executableDirectory, "GenLauncherGO Data", "C&C Zero Hour Data", "Mods"));
    }

    [Fact]
    public void CreateGamePathsRejectsUnknownGame()
    {
        var storage = new LauncherStoragePaths(Path.GetFullPath("Launcher"));

        Action act = () => storage.CreateGamePaths(SupportedGame.Unknown, Path.GetFullPath("Game"));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
