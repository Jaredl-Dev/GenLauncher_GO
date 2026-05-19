using System;
using System.IO;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;

namespace GenLauncherGO.Tests.Core.Startup;

public sealed class LauncherPathsTests
{
    [Fact]
    public void Constructor_NormalizesRootsAndDerivesCanonicalLayout()
    {
        string gameDirectory = Path.Combine("GenLauncherGO.Tests", "Game", ".");
        string executableDirectory = Path.Combine("GenLauncherGO.Tests", "Launcher", ".");

        var storagePaths = new LauncherStoragePaths(executableDirectory);
        LauncherPaths paths = storagePaths.CreateGamePaths(SupportedGame.ZeroHour, gameDirectory);
        string expectedGameRoot = Path.GetFullPath(gameDirectory);
        string expectedOwnedGameDataRoot = Path.Combine(
            storagePaths.DataDirectory,
            "C&C Zero Hour Data");

        paths.Game.Should().Be(SupportedGame.ZeroHour);
        paths.GameDirectory.Should().Be(expectedGameRoot);
        paths.OwnedGameDataDirectory.Should().Be(expectedOwnedGameDataRoot);
        paths.RuntimeDirectory.Should().Be(Path.Combine(expectedOwnedGameDataRoot, "Runtime"));
        paths.CacheDirectory.Should().Be(Path.Combine(expectedOwnedGameDataRoot, "Runtime", "Cache"));
        paths.ImagesDirectory.Should().Be(Path.Combine(expectedOwnedGameDataRoot, "Runtime", "Cache", "Images"));
        paths.ModsDirectory.Should().Be(Path.Combine(expectedOwnedGameDataRoot, "Mods"));
        paths.TempDirectory.Should().Be(Path.Combine(expectedOwnedGameDataRoot, "Runtime", "Temp"));
        paths.DeploymentDirectory.Should().Be(Path.Combine(expectedOwnedGameDataRoot, "Runtime", "Deployment"));
        paths.StateDirectory.Should().Be(Path.Combine(expectedOwnedGameDataRoot, "Runtime", "State"));
        paths.IntegrityDirectory.Should().Be(Path.Combine(expectedOwnedGameDataRoot, "Runtime", "Integrity"));
        paths.PackageBackupsDirectory.Should().Be(Path.Combine(
            expectedOwnedGameDataRoot,
            "Runtime",
            "State",
            LauncherFileSystemLayout.PackageBackupsFolderName));
    }

    [Fact]
    public void GetPackageTemporaryPath_BuildsOwnedPathUnderTempDirectory()
    {
        LauncherPaths paths = TestLauncherPaths.Create();
        string installedFolderPath = Path.Combine(paths.ModsDirectory, "ShockWave", "1.2");

        OwnedContentPath temporaryPath = paths.GetPackageTemporaryPath(
            new OwnedContentPath(paths.ModsDirectory, installedFolderPath));

        temporaryPath.OwnerRoot.Should().Be(paths.PackagesDirectory);
        temporaryPath.FullPath.Should().Be(Path.Combine(paths.TempDirectory, "Packages", "ShockWave", "1.2"));
    }

    [Fact]
    public void GetPackageTemporaryPath_UsesFolderNameWhenInstallIsOutsideModsDirectory()
    {
        LauncherPaths paths = TestLauncherPaths.Create();
        string installedFolderPath = Path.Combine(paths.GameDirectory, "Data");

        OwnedContentPath temporaryPath = paths.GetPackageTemporaryPath(
            new OwnedContentPath(paths.GameDirectory, installedFolderPath));

        temporaryPath.FullPath.Should().Be(Path.Combine(paths.TempDirectory, "Packages", "Data"));
    }

    [Fact]
    public void GetPackageTemporaryPath_PreservesModsChildNamesThatStartWithDots()
    {
        LauncherPaths paths = TestLauncherPaths.Create();
        string installedFolderPath = Path.Combine(paths.ModsDirectory, "..cache", "1.0");

        OwnedContentPath temporaryPath = paths.GetPackageTemporaryPath(
            new OwnedContentPath(paths.ModsDirectory, installedFolderPath));

        temporaryPath.FullPath.Should().Be(Path.Combine(paths.TempDirectory, "Packages", "..cache", "1.0"));
    }

    [Fact]
    public void GetPackageBackupPathMirrorsModsRelativePath_UnderDurableStateDirectory()
    {
        LauncherPaths paths = TestLauncherPaths.Create();
        string installedFolderPath = Path.Combine(paths.ModsDirectory, "ShockWave", "1.2");

        OwnedContentPath backupPath = paths.GetPackageBackupPath(
            new OwnedContentPath(paths.ModsDirectory, installedFolderPath));

        string backupRoot = Path.Combine(
            paths.StateDirectory,
            LauncherFileSystemLayout.PackageBackupsFolderName);
        backupPath.OwnerRoot.Should().Be(backupRoot);
        backupPath.FullPath.Should().Be(Path.Combine(backupRoot, "ShockWave", "1.2"));
    }

    [Fact]
    public void GetPackageBackupPath_RejectsInstallOutsideModsDirectory()
    {
        LauncherPaths paths = TestLauncherPaths.Create();
        var installedPath = new OwnedContentPath(
            paths.GameDirectory,
            Path.Combine(paths.GameDirectory, "Data"));

        Action act = () => paths.GetPackageBackupPath(installedPath);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("installedPath");
    }

    [Fact]
    public void LauncherDataFilePath_BuildsPathUnderRuntimeStateDirectory()
    {
        LauncherPaths paths = TestLauncherPaths.Create();

        string launcherDataFilePath = paths.LauncherDataFilePath;

        launcherDataFilePath.Should().Be(
            Path.Combine(paths.RuntimeDirectory, "State", "LauncherData.yaml"));
    }

    [Fact]
    public void GetModificationImageFilePath_BuildsPathUnderModificationImageCache()
    {
        LauncherPaths paths = TestLauncherPaths.Create();

        string imageFilePath = paths.GetModificationImageFilePath("ShockWave", "1.2.png");

        imageFilePath.Should().Be(Path.Combine(paths.ImagesDirectory, "ShockWave", "1.2.png"));
    }

    [Fact]
    public void GetModificationImagesDirectory_ThrowsForPathTraversalModificationName()
    {
        LauncherPaths paths = TestLauncherPaths.Create();

        Action act = () => paths.GetModificationImagesDirectory($"..{Path.DirectorySeparatorChar}Escape");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetModificationImageFilePath_ThrowsForPathTraversalImageFileName()
    {
        LauncherPaths paths = TestLauncherPaths.Create();

        Action act = () => paths.GetModificationImageFilePath(
            "ShockWave",
            $"..{Path.DirectorySeparatorChar}1.2.png");

        act.Should().Throw<ArgumentException>();
    }

}
