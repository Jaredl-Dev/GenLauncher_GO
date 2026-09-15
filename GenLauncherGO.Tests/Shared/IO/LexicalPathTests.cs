using System;
using System.IO;
using GenLauncherGO.Shared.IO;

namespace GenLauncherGO.Tests.Shared.IO;

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
    public void ResolveContainedPath_RejectsTraversal()
    {
        using TestDirectory directory = new();

        Action act = () => LexicalPath.ResolveContainedPath(
            directory.Path,
            "../Outside/file.txt",
            "The path must remain contained.");

        act.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\Escape")]
    [InlineData("file:stream")]
    [InlineData("na|me")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("ShockWave.")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL.big")]
    [InlineData("COM1")]
    [InlineData("lpt9.ini")]
    public void NormalizePathSegment_RejectsUnsafeSegments(string segment)
    {
        Action act = () => LexicalPath.NormalizePathSegment(segment, nameof(segment));

        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(segment));
    }

    [Theory]
    [InlineData("COM0")]
    [InlineData("COM12")]
    [InlineData("COMX")]
    [InlineData("LPT0")]
    [InlineData("LPT10")]
    public void NormalizePathSegment_AcceptsNamesThatOnlyResembleReservedDevices(string segment)
    {
        string result = LexicalPath.NormalizePathSegment(segment, nameof(segment));

        result.Should().Be(segment);
    }
}
