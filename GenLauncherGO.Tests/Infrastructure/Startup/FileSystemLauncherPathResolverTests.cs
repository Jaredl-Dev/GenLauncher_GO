using System.IO;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Startup;
using GenLauncherGO.Tests.Testing;

namespace GenLauncherGO.Tests.Infrastructure.Startup;

public sealed class FileSystemLauncherPathResolverTests
{
    [Fact]
    public void ResolveAlwaysUsesExecutableDirectoryWithoutInferringAGame()
    {
        using var directory = new TestDirectory();
        var resolver = new FileSystemLauncherPathResolver();

        LauncherStoragePaths paths = resolver.Resolve(directory.Path);

        paths.ExecutableDirectory.Should().Be(Path.GetFullPath(directory.Path));
        paths.DataDirectory.Should().Be(Path.Combine(directory.Path, "GenLauncherGO Data"));
        paths.LogsDirectory.Should().Be(Path.Combine(directory.Path, "GenLauncherGO Data", "Logs"));
        paths.PreferencesFilePath.Should().Be(
            Path.Combine(directory.Path, "GenLauncherGO Data", "LauncherPreferences.yaml"));
    }

    [Fact]
    public void PrepareLauncherDirectoriesCreatesOnlySharedStorage()
    {
        using var directory = new TestDirectory();
        var resolver = new FileSystemLauncherPathResolver();
        LauncherStoragePaths paths = resolver.Resolve(directory.Path);
        string generalsDataDirectory = paths.CreateGamePaths(SupportedGame.Generals, directory.Path)
            .OwnedGameDataDirectory;
        string zeroHourDataDirectory = paths.CreateGamePaths(SupportedGame.ZeroHour, directory.Path)
            .OwnedGameDataDirectory;

        resolver.PrepareLauncherDirectories(paths);

        Directory.Exists(paths.DataDirectory).Should().BeTrue();
        Directory.Exists(paths.LogsDirectory).Should().BeTrue();
        Directory.Exists(generalsDataDirectory).Should().BeFalse();
        Directory.Exists(zeroHourDataDirectory).Should().BeFalse();
    }

    [Fact]
    public void PrepareGameDirectoriesCreatesIsolatedLayoutAndClearsOnlyTemp()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string gameDirectory = directory.CreateDirectory("Game");
        var resolver = new FileSystemLauncherPathResolver();
        LauncherStoragePaths storage = resolver.Resolve(executableDirectory);
        resolver.PrepareLauncherDirectories(storage);
        LauncherPaths paths = storage.CreateGamePaths(SupportedGame.ZeroHour, gameDirectory);
        string staleTempFile = Path.Combine(paths.TempDirectory, "download.part");
        string deploymentJournal = Path.Combine(paths.DeploymentDirectory, "journal.json");
        Directory.CreateDirectory(paths.TempDirectory);
        Directory.CreateDirectory(paths.DeploymentDirectory);
        File.WriteAllText(staleTempFile, string.Empty);
        File.WriteAllText(deploymentJournal, string.Empty);

        resolver.PrepareGameDirectories(paths, cleanTemporaryDirectory: true);

        Directory.Exists(paths.RuntimeDirectory).Should().BeTrue();
        Directory.Exists(paths.CacheDirectory).Should().BeTrue();
        Directory.Exists(paths.ImagesDirectory).Should().BeTrue();
        Directory.Exists(paths.ModsDirectory).Should().BeTrue();
        Directory.Exists(paths.TempDirectory).Should().BeTrue();
        Directory.Exists(paths.DeploymentDirectory).Should().BeTrue();
        Directory.Exists(paths.IntegrityDirectory).Should().BeTrue();
        Directory.Exists(paths.StateDirectory).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(paths.TempDirectory).Should().BeEmpty();
        File.Exists(deploymentJournal).Should().BeTrue();
    }
}
