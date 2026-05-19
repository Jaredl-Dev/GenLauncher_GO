using System.IO;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Tests.Testing;

namespace GenLauncherGO.Tests.Core.IO;

public sealed class LexicalPathTests
{
    [Fact]
    public void ContainmentAcceptsRootAndChildWithWindowsCaseSemantics()
    {
        using TestDirectory directory = new();
        string childPath = Path.Combine(directory.Path, "Child", "file.txt");

        LexicalPath.IsPathInDirectory(directory.Path.ToUpperInvariant(), directory.Path.ToLowerInvariant())
            .Should().BeTrue();
        LexicalPath.IsPathInDirectory(childPath.ToUpperInvariant(), directory.Path.ToLowerInvariant())
            .Should().BeTrue();
    }

    [Fact]
    public void ContainmentRejectsSiblingWithMatchingPrefix()
    {
        using TestDirectory directory = new();
        string siblingPath = directory.Path + "-sibling";

        bool result = LexicalPath.IsPathInDirectory(siblingPath, directory.Path);

        result.Should().BeFalse();
    }

    [Fact]
    public void RelativePathsUseCanonicalSeparatorsAndDistinguishParentSegmentsFromNames()
    {
        using TestDirectory directory = new();
        string childPath = Path.Combine(directory.Path, "Data", "INI", "GameData.ini");
        string outsidePath = Path.Combine(directory.Path, "..", "Outside", "file.txt");

        LexicalPath.GetRelativePath(directory.Path, childPath).Should().Be("Data/INI/GameData.ini");
        LexicalPath.RelativePathLeavesRoot(LexicalPath.GetRelativePath(directory.Path, outsidePath))
            .Should().BeTrue();
        LexicalPath.RelativePathLeavesRoot("../Outside/file.txt").Should().BeTrue();
        LexicalPath.RelativePathLeavesRoot("..cache/file.txt").Should().BeFalse();
    }

    [Fact]
    public void ResolvePathNormalizesTraversalWithoutClaimingContainment()
    {
        using TestDirectory directory = new();
        string resolvedPath = LexicalPath.ResolvePath(directory.Path, "../Outside/file.txt");

        resolvedPath.Should().Be(Path.GetFullPath(Path.Combine(directory.Path, "..", "Outside", "file.txt")));
        LexicalPath.IsPathInDirectory(resolvedPath, directory.Path).Should().BeFalse();
    }

    [Fact]
    public void NormalizeRelativePathUsesSlashSeparatorsWithoutOuterSlashes()
    {
        string result = LexicalPath.NormalizeRelativePath(@"\Data\INI\GameData.ini/");

        result.Should().Be("Data/INI/GameData.ini");
    }
}
