using System.IO;
using GenLauncherGO.Infrastructure.Launching.Support;
using GenLauncherGO.Tests.Testing;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Support;

public sealed class WindowsHardLinkCreatorTests
{
    [Fact]
    public void TryCreateHardLinkCreatesNonReparseHardLinkToExistingFile()
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
    public void TryCreateHardLinkReturnsFalseWhenSourceIsMissing()
    {
        using TestDirectory directory = new();
        string sourcePath = Path.Combine(directory.Path, "missing.big");
        string targetPath = Path.Combine(directory.Path, "target.big");
        WindowsHardLinkCreator creator = new();

        bool created = creator.TryCreateHardLink(targetPath, sourcePath);

        created.Should().BeFalse();
        File.Exists(targetPath).Should().BeFalse();
    }
}
