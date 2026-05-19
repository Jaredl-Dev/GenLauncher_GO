using System;
using System.IO;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Launching.Support;
using GenLauncherGO.Tests.Testing;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Support;

public sealed class DeploymentPathResolverTests
{
    [Theory]
    [InlineData(@"Data\INI\GameData.ini", "Data/INI/GameData.ini")]
    [InlineData(@"Data//INI\\GameData.ini", "Data/INI/GameData.ini")]
    [InlineData(" Data/INI/GameData.ini ", " Data/INI/GameData.ini ")]
    public void NormalizeManifestPathNormalizesSeparators(string relativePath, string expectedPath)
    {
        string result = DeploymentPathResolver.NormalizeManifestPath(relativePath);

        result.Should().Be(expectedPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void NormalizeManifestPathRejectsMissingPaths(string relativePath)
    {
        Action act = () => DeploymentPathResolver.NormalizeManifestPath(relativePath);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(@"C:\Game\Data\GameData.ini", "Deployment manifest paths must be relative.")]
    [InlineData("C:Game/Data/GameData.ini", "Deployment manifest paths must be relative.")]
    [InlineData("../Data/GameData.ini", "Deployment manifest paths must not contain parent directory segments.")]
    [InlineData("./Data/GameData.ini", "Deployment manifest paths must not contain parent directory segments.")]
    public void NormalizeManifestPathRejectsUnsafePaths(string relativePath, string expectedMessage)
    {
        Action act = () => DeploymentPathResolver.NormalizeManifestPath(relativePath);

        act.Should().Throw<InvalidDataException>()
            .WithMessage(expectedMessage);
    }

    [Fact]
    public void ResolveGamePathReturnsPathInsideGameDirectory()
    {
        using TestDirectory directory = new();
        LauncherPaths paths = CreatePaths(directory);

        string result = DeploymentPathResolver.ResolveGamePath(paths, @"Data\GameData.ini");

        result.Should().Be(Path.GetFullPath(Path.Combine(paths.GameDirectory, "Data", "GameData.ini")));
    }

    [Fact]
    public void ResolveGamePathRejectsLauncherOwnedPaths()
    {
        using TestDirectory directory = new();
        string gameDirectory = directory.CreateDirectory("Game");
        string executableDirectory = directory.CreateDirectory(Path.Combine("Game", "GenLauncherGO"));
        LauncherPaths paths = new LauncherStoragePaths(executableDirectory)
            .CreateGamePaths(SupportedGame.ZeroHour, gameDirectory);
        string launcherOwnedPath = Path.GetRelativePath(
            paths.GameDirectory,
            Path.Combine(paths.RuntimeDirectory, "state.yaml"));

        Action act = () => DeploymentPathResolver.ResolveGamePath(
            paths,
            launcherOwnedPath);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*outside the game directory*");
    }

    [Fact]
    public void ToRelativeManifestPathReturnsNormalizedChildPath()
    {
        using TestDirectory directory = new();
        string rootDirectory = Path.Combine(directory.Path, "Package");
        string path = Path.Combine(rootDirectory, "Data", "GameData.ini");

        string result = DeploymentPathResolver.ToRelativeManifestPath(rootDirectory, path);

        result.Should().Be("Data/GameData.ini");
    }

    [Fact]
    public void ToRelativeManifestPathRejectsPathsOutsideRoot()
    {
        using TestDirectory directory = new();
        string rootDirectory = Path.Combine(directory.Path, "Package");
        string path = Path.Combine(directory.Path, "Other", "GameData.ini");

        Action act = () => DeploymentPathResolver.ToRelativeManifestPath(rootDirectory, path);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ResolveDeploymentStatePathReturnsPathInsideDeploymentDirectory()
    {
        using TestDirectory directory = new();
        string deploymentDirectory = Path.Combine(directory.Path, "Deployment");

        string result = DeploymentPathResolver.ResolveDeploymentStatePath(
            deploymentDirectory,
            @"Records\manifest.yaml");

        result.Should().Be(Path.GetFullPath(Path.Combine(deploymentDirectory, "Records", "manifest.yaml")));
    }

    [Fact]
    public void ResolveDeploymentStatePathRejectsPathsOutsideDeploymentDirectory()
    {
        using TestDirectory directory = new();
        string deploymentDirectory = Path.Combine(directory.Path, "Deployment");

        Action act = () => DeploymentPathResolver.ResolveDeploymentStatePath(
            deploymentDirectory,
            "../manifest.yaml");

        act.Should().Throw<InvalidDataException>();
    }

    private static LauncherPaths CreatePaths(TestDirectory directory)
    {
        return TestLauncherPaths.Create(Path.Combine(directory.Path, "Game"));
    }
}
