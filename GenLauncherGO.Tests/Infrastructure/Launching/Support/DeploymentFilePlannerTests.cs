using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenLauncherGO.Infrastructure.Launching.Support;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Support;

public sealed class DeploymentFilePlannerTests
{
    [Fact]
    public void ResolveDeploymentFiles_ExcludesExecutableCodeFromDownloadedPackages()
    {
        using TestDirectory directory = new();
        string packageRoot = directory.CreateDirectory("Package");
        string dataDirectory = Directory.CreateDirectory(Path.Combine(packageRoot, "Data")).FullName;
        File.WriteAllText(Path.Combine(dataDirectory, "b.ini"), "second");
        File.WriteAllText(Path.Combine(dataDirectory, "a.ini"), "first");
        File.WriteAllText(Path.Combine(packageRoot, "Zeta.big"), "archive");
        File.WriteAllText(Path.Combine(packageRoot, "community-client.EXE"), "executable");
        File.WriteAllText(Path.Combine(packageRoot, "community-plugin.DlL"), "library");

        IReadOnlyList<ResolvedDeploymentFile> result = DeploymentFilePlanner.ResolveDeploymentFiles(
            new[] { new DeploymentPackage(packageRoot, 0) });

        result.Select(file => file.TargetRelativePath)
            .Should()
            .Equal("Data/a.ini", "Data/b.ini", "Zeta.big");
    }

    [Fact]
    public void ResolveDeploymentFiles_HigherPrecedencePackageReplacesSameTarget()
    {
        using TestDirectory directory = new();
        string modRoot = directory.CreateDirectory("Mod");
        string addonRoot = directory.CreateDirectory("Addon");
        Directory.CreateDirectory(Path.Combine(modRoot, "Data"));
        Directory.CreateDirectory(Path.Combine(addonRoot, "Data"));
        File.WriteAllText(Path.Combine(modRoot, "Data", "file.ini"), "mod");
        string addonSourcePath = Path.Combine(addonRoot, "Data", "file.ini");
        File.WriteAllText(addonSourcePath, "addon");

        IReadOnlyList<ResolvedDeploymentFile> result = DeploymentFilePlanner.ResolveDeploymentFiles(
            new[]
            {
                new DeploymentPackage(addonRoot, 1),
                new DeploymentPackage(modRoot, 0)
            });

        result.Should().ContainSingle().Which.Should().Match<ResolvedDeploymentFile>(file =>
            file.TargetRelativePath == "Data/file.ini" &&
            file.SourcePath == addonSourcePath);
    }

    [Fact]
    public void ResolveDeploymentFiles_WhenAPackageDirectoryIsMissing_ReportsTheMissingDirectory()
    {
        using TestDirectory directory = new();
        string packageRoot = Path.Combine(directory.Path, "Missing");

        Action resolve = () => DeploymentFilePlanner.ResolveDeploymentFiles(
            new[] { new DeploymentPackage(packageRoot, 0) });

        resolve.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void ResolveDeploymentFiles_WhenThePackageRootIsALink_RefusesToReadThroughIt()
    {
        using TestDirectory directory = new();
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(
            directory,
            Path.Combine(directory.Path, "Package"));

        Action resolve = () => DeploymentFilePlanner.ResolveDeploymentFiles(
            new[] { new DeploymentPackage(junction.JunctionPath, 0) });

        resolve.Should().Throw<InvalidDataException>();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public void GetDirectoriesToCreate_ReturnsMissingDirectoriesParentFirst()
    {
        using TestDirectory directory = new();
        string gameRoot = directory.CreateDirectory("Game");
        string targetDirectory = Path.Combine(gameRoot, "Data", "Sub");

        IEnumerable<string> directories = DeploymentFilePlanner.GetDirectoriesToCreate(gameRoot, targetDirectory);

        directories.Should().Equal(
            Path.Combine(gameRoot, "Data"),
            targetDirectory);
    }

    [Fact]
    public void GetDirectoriesToCreate_WhenTargetDirectoryExists_ReturnsNothing()
    {
        using TestDirectory directory = new();
        string gameRoot = directory.CreateDirectory("Game");
        string targetDirectory = Directory.CreateDirectory(Path.Combine(gameRoot, "Data", "Sub")).FullName;

        IEnumerable<string> directories = DeploymentFilePlanner.GetDirectoriesToCreate(gameRoot, targetDirectory);

        directories.Should().BeEmpty();
    }
}
