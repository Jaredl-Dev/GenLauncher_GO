using System;
using System.IO;
using GenLauncherGO.Core.IO;

namespace GenLauncherGO.Tests.Core.IO;

public sealed class LexicalPathTests
{
    [Fact]
    public void AreEquivalent_NormalizesCaseTrailingSeparatorsAndDotSegments()
    {
        using TestDirectory directory = new();
        string equivalentPath = Path.Combine(directory.Path.ToUpperInvariant(), ".", "Child", "..");

        bool result = LexicalPath.AreEquivalent(
            directory.Path.ToLowerInvariant() + Path.DirectorySeparatorChar,
            equivalentPath);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, @"C:\Games\ZeroHour")]
    [InlineData(@"C:\Games\ZeroHour", null)]
    [InlineData("", @"C:\Games\ZeroHour")]
    [InlineData("   ", @"C:\Games\ZeroHour")]
    [InlineData(@"C:\Games\Generals", @"C:\Games\ZeroHour")]
    public void AreEquivalent_WhenEitherPathIsMissingOrDifferent_ReturnsFalse(
        string? left,
        string? right)
    {
        LexicalPath.AreEquivalent(left, right).Should().BeFalse();
    }

    [Fact]
    public void AreEquivalent_WhenAPathIsMalformed_ReturnsFalse()
    {
        const string MalformedPath = "invalid\0path";

        bool result = LexicalPath.AreEquivalent(MalformedPath, MalformedPath);

        result.Should().BeFalse();
    }

    [Fact]
    public void Containment_AcceptsRootAndChildWithWindowsCaseSemantics()
    {
        using TestDirectory directory = new();
        string childPath = Path.Combine(directory.Path, "Child", "file.txt");

        LexicalPath.IsPathInDirectory(directory.Path.ToUpperInvariant(), directory.Path.ToLowerInvariant())
            .Should().BeTrue();
        LexicalPath.IsPathInDirectory(childPath.ToUpperInvariant(), directory.Path.ToLowerInvariant())
            .Should().BeTrue();
    }

    [Fact]
    public void Containment_RejectsSiblingWithMatchingPrefix()
    {
        using TestDirectory directory = new();
        string siblingPath = directory.Path + "-sibling";

        bool result = LexicalPath.IsPathInDirectory(siblingPath, directory.Path);

        result.Should().BeFalse();
    }

    [Fact]
    public void RelativePaths_SeparatorsAndParentSegments_AreCanonicalAndDistinct()
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
    public void ResolvePath_NormalizesTraversalWithoutClaimingContainment()
    {
        using TestDirectory directory = new();
        string resolvedPath = LexicalPath.ResolvePath(directory.Path, "../Outside/file.txt");

        resolvedPath.Should().Be(Path.GetFullPath(Path.Combine(directory.Path, "..", "Outside", "file.txt")));
        LexicalPath.IsPathInDirectory(resolvedPath, directory.Path).Should().BeFalse();
    }

    [Fact]
    public void ResolveContainedPath_AcceptsAChildPath()
    {
        using TestDirectory directory = new();

        string result = LexicalPath.ResolveContainedPath(
            directory.Path,
            "Child/file.txt",
            "The path must remain contained.");

        result.Should().Be(Path.Combine(directory.Path, "Child", "file.txt"));
    }

    [Fact]
    public void ResolveContainedPath_RejectsTraversal()
    {
        using TestDirectory directory = new();

        Action act = () => LexicalPath.ResolveContainedPath(
            directory.Path,
            "../Outside/file.txt",
            "The path must remain contained.");

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void NormalizeRelativePath_UsesSlashSeparatorsWithoutOuterSlashes()
    {
        string result = LexicalPath.NormalizeRelativePath(@"\Data\INI\GameData.ini/");

        result.Should().Be("Data/INI/GameData.ini");
    }

    [Fact]
    public void Containment_AcceptsChildOfADriveRoot()
    {
        using TestDirectory directory = new();

        LexicalPath.IsPathInDirectory(@"C:\Games\ZeroHour", @"C:\").Should().BeTrue();
        LexicalPath.IsPathInDirectory(directory.Path, directory.Path + Path.DirectorySeparatorChar)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\Escape")]
    [InlineData("file:stream")]
    [InlineData("na|me")]
    [InlineData("<name")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("ShockWave.")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL.big")]
    [InlineData("COM1")]
    [InlineData("com1")]
    [InlineData("lpt9.ini")]
    public void NormalizePathSegment_RejectsUnsafeSegments(string segment)
    {
        Action act = () => LexicalPath.NormalizePathSegment(segment, nameof(segment));

        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(segment));
    }

    [Theory]
    [InlineData("ABC1")]
    [InlineData("COM0")]
    [InlineData("COM12")]
    [InlineData("COMX")]
    [InlineData("LPT0")]
    [InlineData("LPT10")]
    [InlineData("LPTX")]
    public void NormalizePathSegment_AcceptsNamesThatOnlyResembleReservedDevices(string segment)
    {
        string result = LexicalPath.NormalizePathSegment(segment, nameof(segment));

        result.Should().Be(segment);
    }
}
