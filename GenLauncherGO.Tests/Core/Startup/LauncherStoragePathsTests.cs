using System;
using System.IO;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Tests.Core.Startup;

public sealed class LauncherStoragePathsTests
{
    [Theory]
    [InlineData(SupportedGame.Generals, "C&C Generals Data")]
    [InlineData(SupportedGame.ZeroHour, "C&C Zero Hour Data")]
    public void Constructor_DerivesSharedStandaloneAndIsolatedGamePaths(
        SupportedGame game,
        string expectedGameDataFolderName)
    {
        string executableDirectory = Path.GetFullPath(Path.Combine("GenLauncherGO.Tests", "Launcher"));
        string dataDirectory = Path.Combine(
            executableDirectory,
            LauncherFileSystemLayout.LauncherDataFolderName);
        var storage = new LauncherStoragePaths(executableDirectory);

        LauncherPaths gamePaths = storage.CreateGamePaths(
            game,
            Path.GetFullPath(Path.Combine("GenLauncherGO.Tests", "Game")));

        storage.ExecutableDirectory.Should().Be(executableDirectory);
        storage.DataDirectory.Should().Be(dataDirectory);
        storage.LogsDirectory.Should().Be(Path.Combine(dataDirectory, "Logs"));
        storage.PreferencesFilePath.Should().Be(Path.Combine(dataDirectory, "LauncherPreferences.yaml"));
        gamePaths.OwnedGameDataDirectory.Should().Be(Path.Combine(dataDirectory, expectedGameDataFolderName));
    }

    [Fact]
    public void CreateGamePaths_IsolatesEachSupportedGameDataDirectory()
    {
        var storage = new LauncherStoragePaths(
            Path.GetFullPath(Path.Combine("GenLauncherGO.Tests", "Launcher")));
        string gameDirectory = Path.GetFullPath(Path.Combine("GenLauncherGO.Tests", "Game"));

        LauncherPaths generalsPaths = storage.CreateGamePaths(SupportedGame.Generals, gameDirectory);
        LauncherPaths zeroHourPaths = storage.CreateGamePaths(SupportedGame.ZeroHour, gameDirectory);

        generalsPaths.OwnedGameDataDirectory.Should().NotBe(zeroHourPaths.OwnedGameDataDirectory);
    }

    [Fact]
    public void CreateGamePaths_WithUnsupportedGame_Throws()
    {
        var storage = new LauncherStoragePaths(Path.GetFullPath("Launcher"));

        Action act = () => storage.CreateGamePaths(SupportedGame.Unknown, Path.GetFullPath("Game"));

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("managedGame");
    }
}
