using System;
using System.IO;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Tests.Testing;

namespace GenLauncherGO.Tests.Infrastructure.Common;

public sealed class FileSystemPathSafetyTests
{
    [Fact]
    public void ResolveOwnedSubpathReturnsNormalizedChildPath()
    {
        using TestDirectory directory = new();
        string candidatePath = Path.Combine(directory.Path, "Child", "..", "Child", "file.txt");

        string result = FileSystemPathSafety.ResolveOwnedSubpath(
            directory.Path,
            candidatePath,
            "outside",
            "linked");

        result.Should().Be(Path.GetFullPath(Path.Combine(directory.Path, "Child", "file.txt")));
    }

    [Fact]
    public void ResolveOwnedSubpathRejectsPathOutsideOwnedRoot()
    {
        using TestDirectory directory = new();
        string outsidePath = Path.Combine(directory.Path, "..", "outside.txt");

        Action act = () => FileSystemPathSafety.ResolveOwnedSubpath(
            directory.Path,
            outsidePath,
            "outside root",
            "linked path");

        act.Should().Throw<InvalidDataException>()
            .WithMessage("outside root");
    }

    [Fact]
    public void ExistingPathChainContainsReparsePointReturnsFalseForRootAndMissingChild()
    {
        using TestDirectory directory = new();
        string missingChild = Path.Combine(directory.Path, "Missing", "file.txt");

        FileSystemPathSafety.ExistingPathChainContainsReparsePoint(
            Path.GetPathRoot(directory.Path)!,
            "unrooted").Should().BeFalse();
        FileSystemPathSafety.ExistingPathChainContainsReparsePoint(
            missingChild,
            "unrooted").Should().BeFalse();
    }

    [Fact]
    public void EnsureExistingPathChainHasNoReparsePointsAllowsNormalFiles()
    {
        using TestDirectory directory = new();
        string filePath = Path.Combine(directory.Path, "file.txt");
        File.WriteAllText(filePath, "content");

        Action act = () => FileSystemPathSafety.EnsureExistingPathChainHasNoReparsePoints(
            filePath,
            "unrooted",
            "linked");

        act.Should().NotThrow();
        FileSystemPathSafety.IsReparsePoint(filePath).Should().BeFalse();
    }

    [SymbolicLinkFact]
    public void EnsureDirectoryTreeHasNoReparsePointsRejectsChildReparsePoint()
    {
        using TestDirectory directory = new();
        string rootPath = Path.Combine(directory.Path, "Root");
        string linkedTarget = Path.Combine(directory.Path, "Target");
        string linkPath = Path.Combine(rootPath, "Linked");
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(linkedTarget);
        SymbolicLinkTestSupport.CreateDirectoryLink(linkPath, linkedTarget);

        Action act = () => FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(rootPath, "linked");

        act.Should().Throw<InvalidDataException>()
            .WithMessage("linked");
    }

    [Fact]
    public void CreateRecursiveNoLinksOptionsSkipsReparsePoints()
    {
        EnumerationOptions result = FileSystemPathSafety.CreateRecursiveNoLinksOptions();

        result.AttributesToSkip.Should().Be(FileAttributes.ReparsePoint);
        result.IgnoreInaccessible.Should().BeFalse();
        result.RecurseSubdirectories.Should().BeTrue();
        result.ReturnSpecialDirectories.Should().BeFalse();
    }

}
