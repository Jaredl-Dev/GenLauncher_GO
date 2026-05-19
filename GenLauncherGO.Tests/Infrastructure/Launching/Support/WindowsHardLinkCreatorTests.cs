using System.IO;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Launching.Support;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Support;

public sealed class WindowsHardLinkCreatorTests
{
    private const string ExtendedLengthPrefix = @"\\?\";

    [Fact]
    public void ArePathsOnSameVolume_ReturnsTrueForFilesUnderSameVolume()
    {
        using TestDirectory directory = new();
        string firstPath = Path.Combine(directory.Path, "First", "one.big");
        string secondPath = Path.Combine(directory.Path, "Second", "two.big");
        Directory.CreateDirectory(Path.GetDirectoryName(firstPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(secondPath)!);
        WindowsHardLinkCreator creator = new();

        bool sameVolume = creator.ArePathsOnSameVolume(firstPath, secondPath);

        sameVolume.Should().BeTrue();
    }

    [Fact]
    public void TryCreateHardLink_CreatesNonReparseHardLinkToExistingFile()
    {
        using TestDirectory directory = new();
        string sourcePath = Path.Combine(directory.Path, "source.big");
        string targetPath = Path.Combine(directory.Path, "target.big");
        File.WriteAllText(sourcePath, "package");
        WindowsHardLinkCreator creator = new();

        bool created = creator.TryCreateHardLink(targetPath, sourcePath);

        created.Should().BeTrue();
        File.Exists(targetPath).Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("package");
        File.GetAttributes(targetPath).Should().NotHaveFlag(FileAttributes.ReparsePoint);

        File.WriteAllText(sourcePath, "updated through source");
        File.ReadAllText(targetPath).Should().Be("updated through source");

        File.WriteAllText(targetPath, "updated through target");
        File.ReadAllText(sourcePath).Should().Be("updated through target");
    }

    [Fact]
    public void TryCreateHardLink_ReturnsFalseWhenSourceIsMissing()
    {
        using TestDirectory directory = new();
        string sourcePath = Path.Combine(directory.Path, "missing.big");
        string targetPath = Path.Combine(directory.Path, "target.big");
        WindowsHardLinkCreator creator = new();

        bool created = creator.TryCreateHardLink(targetPath, sourcePath);

        created.Should().BeFalse();
        File.Exists(targetPath).Should().BeFalse();
    }

    /// <summary>
    ///     A caller may already hold Win32 extended-length paths, which must not be prefixed a second time.
    /// </summary>
    [Fact]
    public void TryCreateHardLink_CreatesLinkWhenPathsAreAlreadyExtendedLength()
    {
        using TestDirectory directory = new();
        string sourcePath = Path.Combine(directory.Path, "source.big");
        string targetPath = Path.Combine(directory.Path, "target.big");
        File.WriteAllText(sourcePath, "package");
        WindowsHardLinkCreator creator = new();

        bool created = creator.TryCreateHardLink(
            ExtendedLengthPrefix + targetPath,
            ExtendedLengthPrefix + sourcePath);

        created.Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("package");
        PhysicalDirectoryPath.GetFileIdentity(targetPath).Should()
            .Be(PhysicalDirectoryPath.GetFileIdentity(sourcePath));
    }

    [Fact]
    public void TryCreateHardLink_CreatesLinkWhenSourcePathExceedsLegacyMaxPath()
    {
        using TestDirectory directory = new();
        string longDirectory = directory.Path;
        while (longDirectory.Length < 275)
        {
            longDirectory = Path.Combine(longDirectory, "long-package-directory");
        }

        Directory.CreateDirectory(longDirectory);
        string sourcePath = Path.Combine(longDirectory, "source.big");
        string targetPath = Path.Combine(directory.Path, "target.big");
        File.WriteAllText(sourcePath, "package");
        WindowsHardLinkCreator creator = new();

        bool created = creator.TryCreateHardLink(targetPath, sourcePath);

        sourcePath.Length.Should().BeGreaterThan(260);
        created.Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("package");
        PhysicalDirectoryPath.GetFileIdentity(targetPath).Should()
            .Be(PhysicalDirectoryPath.GetFileIdentity(sourcePath));
    }
}
