using System.IO;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Tests.Testing;

namespace GenLauncherGO.Tests.Infrastructure.Common;

public sealed class BigFileVariantPathTests
{
    [Fact]
    public void VariantMappingsRoundTripCaseInsensitivePackagePathsAndPreserveOtherFiles()
    {
        string bigPath = Path.Combine("Data", "asset.BIG");
        string installedPath = BigFileVariantPath.GetInstalledPath(bigPath);

        installedPath.Should().Be(Path.Combine("Data", "asset.gib"));
        BigFileVariantPath.GetDeploymentPath(installedPath)
            .Should().Be(Path.Combine("Data", "asset.big"));
        BigFileVariantPath.GetInstalledPath("readme.txt").Should().Be("readme.txt");
        BigFileVariantPath.GetDeploymentPath("readme.txt").Should().Be("readme.txt");
    }

    [Fact]
    public void GetExistingDownloadedPathPrefersRequestedBigPath()
    {
        using TestDirectory testDirectory = new();
        string bigPath = Path.Combine(testDirectory.Path, "asset.big");
        string gibPath = Path.Combine(testDirectory.Path, "asset.gib");
        File.WriteAllText(bigPath, "big");
        File.WriteAllText(gibPath, "gib");

        string existingPath = BigFileVariantPath.GetExistingDownloadedPath(bigPath);

        existingPath.Should().Be(bigPath);
    }

    [Fact]
    public void GetExistingDownloadedPathFallsBackToConvertedGibPath()
    {
        using TestDirectory testDirectory = new();
        string bigPath = Path.Combine(testDirectory.Path, "asset.big");
        string gibPath = Path.Combine(testDirectory.Path, "asset.gib");
        File.WriteAllText(gibPath, "gib");

        string existingPath = BigFileVariantPath.GetExistingDownloadedPath(bigPath);

        existingPath.Should().Be(gibPath);
    }

    [Fact]
    public void ConvertBigFileToGibMovesBigFileAndReplacesExistingGib()
    {
        using TestDirectory testDirectory = new();
        string bigPath = Path.Combine(testDirectory.Path, "asset.big");
        string gibPath = Path.Combine(testDirectory.Path, "asset.gib");
        File.WriteAllText(bigPath, "new");
        File.WriteAllText(gibPath, "old");

        BigFileVariantPath.ConvertBigFileToGib(bigPath);

        File.Exists(bigPath).Should().BeFalse();
        File.ReadAllText(gibPath).Should().Be("new");
    }

    [Fact]
    public void PrepareBigFileResumePathMovesConvertedGibBackToBigPath()
    {
        using TestDirectory testDirectory = new();
        string bigPath = Path.Combine(testDirectory.Path, "asset.big");
        string gibPath = Path.Combine(testDirectory.Path, "asset.gib");
        File.WriteAllText(gibPath, "partial");

        BigFileVariantPath.PrepareBigFileResumePath(bigPath);

        File.ReadAllText(bigPath).Should().Be("partial");
        File.Exists(gibPath).Should().BeFalse();
    }

    [Fact]
    public void PrepareBigFileResumePathReturnsWhenResumeMoveIsNotNeeded()
    {
        using TestDirectory testDirectory = new();
        string bigPath = Path.Combine(testDirectory.Path, "asset.big");
        string gibPath = Path.Combine(testDirectory.Path, "asset.gib");
        File.WriteAllText(bigPath, "existing");

        BigFileVariantPath.PrepareBigFileResumePath(bigPath);

        File.ReadAllText(bigPath).Should().Be("existing");
        File.Exists(gibPath).Should().BeFalse();
    }
}
