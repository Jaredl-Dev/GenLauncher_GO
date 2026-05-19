using System.IO;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Startup;

namespace GenLauncherGO.Tests.Infrastructure.Startup;

public sealed class FileSystemLauncherPathResolverTests
{
    [Fact]
    public void Resolve_UsesExecutableDirectoryWithoutInferringAGame()
    {
        using var directory = new TestDirectory();
        var resolver = new FileSystemLauncherPathResolver();

        LauncherStoragePaths paths = resolver.Resolve(directory.Path);

        paths.ExecutableDirectory.Should().Be(Path.GetFullPath(directory.Path));
    }

    [Fact]
    public void PrepareLauncherDirectories_CreatesOnlySharedStorage()
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
    public void PrepareGameDirectories_CreatesIsolatedLayoutAndClearsOnlyTemp()
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

        resolver.PrepareGameDirectories(paths, true);

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

    /// <summary>
    ///     Skipping the temporary cleanup is still a full preparation. The staging and deployment folders are created
    ///     nowhere else, so a caller that keeps existing temporary content still needs them on disk afterwards.
    /// </summary>
    [Fact]
    public void PrepareGameDirectories_WithoutClearingTemporaryContent_StillCreatesTheIsolatedLayout()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string gameDirectory = directory.CreateDirectory("Game");
        var resolver = new FileSystemLauncherPathResolver();
        LauncherStoragePaths storage = resolver.Resolve(executableDirectory);
        resolver.PrepareLauncherDirectories(storage);
        LauncherPaths paths = storage.CreateGamePaths(SupportedGame.Generals, gameDirectory);

        resolver.PrepareGameDirectories(paths, false);

        Directory.Exists(paths.TempDirectory).Should().BeTrue();
        Directory.Exists(paths.DeploymentDirectory).Should().BeTrue();
        Directory.Exists(paths.ModsDirectory).Should().BeTrue();
        Directory.Exists(paths.StateDirectory).Should().BeTrue();
    }

    /// <summary>
    ///     A download the launcher suspended on close leaves its partial content staged under the temporary packages
    ///     folder. Clearing that on the next startup is what would silently restart the transfer from zero.
    /// </summary>
    [Fact]
    public void PrepareGameDirectories_KeepsStagedPackagesWhileClearingOtherTemporaryContent()
    {
        using var directory = new TestDirectory();
        string executableDirectory = directory.CreateDirectory("Launcher");
        string gameDirectory = directory.CreateDirectory("Game");
        var resolver = new FileSystemLauncherPathResolver();
        LauncherStoragePaths storage = resolver.Resolve(executableDirectory);
        resolver.PrepareLauncherDirectories(storage);
        LauncherPaths paths = storage.CreateGamePaths(SupportedGame.ZeroHour, gameDirectory);
        string stagedPackageFile = Path.Combine(paths.PackagesDirectory, "Contra", "contra.big");
        string staleTempFile = Path.Combine(paths.TempDirectory, "scratch.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedPackageFile)!);
        Directory.CreateDirectory(paths.TempDirectory);
        File.WriteAllText(stagedPackageFile, "partial");
        File.WriteAllText(staleTempFile, string.Empty);

        resolver.PrepareGameDirectories(paths, true);

        File.Exists(stagedPackageFile).Should().BeTrue();
        File.ReadAllText(stagedPackageFile).Should().Be("partial");
        File.Exists(staleTempFile).Should().BeFalse();
    }
}
