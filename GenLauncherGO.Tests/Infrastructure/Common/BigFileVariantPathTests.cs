using System.IO;
using GenLauncherGO.Infrastructure.Common;

namespace GenLauncherGO.Tests.Infrastructure.Common;

public sealed class BigFileVariantPathTests
{
    [Fact]
    public void VariantMappings_RoundTripCaseInsensitivePackagePathsAndPreserveOtherFiles()
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
    public void GetExistingDownloadedPath_PrefersRequestedBigPath()
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
    public void GetExistingDownloadedPath_FallsBackToConvertedGibPath()
    {
        using TestDirectory testDirectory = new();
        string bigPath = Path.Combine(testDirectory.Path, "asset.big");
        string gibPath = Path.Combine(testDirectory.Path, "asset.gib");
        File.WriteAllText(gibPath, "gib");

        string existingPath = BigFileVariantPath.GetExistingDownloadedPath(bigPath);

        existingPath.Should().Be(gibPath);
    }

    [Fact]
    public void ConvertBigFileToGib_MovesBigFileAndReplacesExistingGib()
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
    public void PrepareBigFileResumePath_MovesConvertedGibBackToBigPath()
    {
        using TestDirectory testDirectory = new();
        string bigPath = Path.Combine(testDirectory.Path, "asset.big");
        string gibPath = Path.Combine(testDirectory.Path, "asset.gib");
        File.WriteAllText(gibPath, "partial");

        BigFileVariantPath.PrepareBigFileResumePath(bigPath);

        File.ReadAllText(bigPath).Should().Be("partial");
        File.Exists(gibPath).Should().BeFalse();
    }

    /// <summary>
    ///     A partial <c>.big</c> download outranks an installed <c>.gib</c> of the same package, because moving the
    ///     installed file over it would discard the bytes the resumed transfer is about to append to.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrepareBigFileResumePath_ReturnsWhenResumeMoveIsNotNeeded(bool installedFileAlsoExists)
    {
        using TestDirectory testDirectory = new();
        string bigPath = Path.Combine(testDirectory.Path, "asset.big");
        string gibPath = Path.Combine(testDirectory.Path, "asset.gib");
        File.WriteAllText(bigPath, "partial");
        if (installedFileAlsoExists)
        {
            File.WriteAllText(gibPath, "installed");
        }

        BigFileVariantPath.PrepareBigFileResumePath(bigPath);

        File.ReadAllText(bigPath).Should().Be("partial");
        File.Exists(gibPath).Should().Be(installedFileAlsoExists);
    }

    [Fact]
    public void BigFileConversions_NonPackageFile_LeaveTheFileUntouched()
    {
        using TestDirectory testDirectory = new();
        string filePath = Path.Combine(testDirectory.Path, "readme.txt");
        File.WriteAllText(filePath, "readme");

        BigFileVariantPath.ConvertBigFileToGib(filePath);
        BigFileVariantPath.PrepareBigFileResumePath(filePath);

        File.ReadAllText(filePath).Should().Be("readme");
        Directory.EnumerateFileSystemEntries(testDirectory.Path).Should().ContainSingle()
            .Which.Should().Be(filePath);
    }

    /// <summary>
    ///     The <c>.gib</c> rename belongs to packages alone. A same-named installed package beside a non-package
    ///     download must not be dragged into a resume it has nothing to do with.
    /// </summary>
    [Fact]
    public void PrepareBigFileResumePath_NonPackageFile_LeavesTheInstalledPackageAlone()
    {
        using TestDirectory testDirectory = new();
        string textPath = Path.Combine(testDirectory.Path, "readme.txt");
        string gibPath = Path.Combine(testDirectory.Path, "readme.gib");
        File.WriteAllText(gibPath, "installed");

        BigFileVariantPath.PrepareBigFileResumePath(textPath);

        File.ReadAllText(gibPath).Should().Be("installed");
        File.Exists(textPath).Should().BeFalse();
    }
}
