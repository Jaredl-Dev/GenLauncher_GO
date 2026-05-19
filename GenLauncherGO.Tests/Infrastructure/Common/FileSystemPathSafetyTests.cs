using System;
using System.Collections.Generic;
using System.IO;
using GenLauncherGO.Infrastructure.Common;

namespace GenLauncherGO.Tests.Infrastructure.Common;

public sealed class FileSystemPathSafetyTests
{
    // Callers name the paths they guard and the checks build the sentence, so these assertions pin the generated
    // wording rather than a message the test supplied.
    private const string PathSubject = "Test paths";

    private const string OwnerDescription = "the owned root";

    private const string LinkedMessage = "Test paths must not contain reparse points.";

    [Fact]
    public void ResolveOwnedSubpath_ReturnsNormalizedChildPath()
    {
        using TestDirectory directory = new();
        string candidatePath = Path.Combine(directory.Path, "Child", "..", "Child", "file.txt");

        string result = FileSystemPathSafety.ResolveOwnedSubpath(
            directory.Path,
            candidatePath,
            PathSubject,
            OwnerDescription);

        result.Should().Be(Path.GetFullPath(Path.Combine(directory.Path, "Child", "file.txt")));
    }

    [Fact]
    public void ResolveOwnedSubpath_RejectsPathOutsideOwnedRoot()
    {
        using TestDirectory directory = new();
        string outsidePath = Path.Combine(directory.Path, "..", "outside.txt");

        Action act = () => FileSystemPathSafety.ResolveOwnedSubpath(
            directory.Path,
            outsidePath,
            PathSubject,
            OwnerDescription);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("Test paths must stay inside the owned root.");
    }

    [Fact]
    public void ResolveOwnedSubpath_LinkedOwnedRoot_RejectsPath()
    {
        using TestDirectory directory = new();
        string linkedRoot = Path.Combine(directory.Path, "LinkedRoot");
        string targetPath = Path.Combine(directory.Path, "Target");
        ReparsePointTestSupport.CreateDirectoryJunction(linkedRoot, targetPath);

        Action act = () => FileSystemPathSafety.ResolveOwnedSubpath(
            linkedRoot,
            Path.Combine(linkedRoot, "file.txt"),
            PathSubject,
            OwnerDescription);

        act.Should().Throw<InvalidDataException>()
            .WithMessage(LinkedMessage);

        Directory.Delete(linkedRoot, false);
    }

    [Fact]
    public void ResolveOwnedSubpath_RejectsLinkedCandidateAncestor()
    {
        using TestDirectory directory = new();
        string targetPath = Path.Combine(directory.Path, "Target");
        string linkPath = Path.Combine(directory.Path, "Linked");
        ReparsePointTestSupport.CreateDirectoryJunction(linkPath, targetPath);

        Action act = () => FileSystemPathSafety.ResolveOwnedSubpath(
            directory.Path,
            Path.Combine(linkPath, "file.txt"),
            PathSubject,
            OwnerDescription);

        act.Should().Throw<InvalidDataException>()
            .WithMessage(LinkedMessage);
        FileSystemPathSafety.IsReparsePoint(linkPath).Should().BeTrue();

        Directory.Delete(linkPath, false);
    }

    [Fact]
    public void ExistingPathChainContainsReparsePoint_ReturnsFalseForRootAndMissingChild()
    {
        using TestDirectory directory = new();
        string missingChild = Path.Combine(directory.Path, "Missing", "file.txt");

        FileSystemPathSafety.ExistingPathChainContainsReparsePoint(
            Path.GetPathRoot(directory.Path)!,
            PathSubject).Should().BeFalse();
        FileSystemPathSafety.ExistingPathChainContainsReparsePoint(
            missingChild,
            PathSubject).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoryTreeApis_RootReparsePoint_RejectTree(bool enumerateFiles)
    {
        using TestDirectory directory = new();
        string linkedRoot = Path.Combine(directory.Path, "LinkedRoot");
        string targetPath = Path.Combine(directory.Path, "Target");
        ReparsePointTestSupport.CreateDirectoryJunction(linkedRoot, targetPath);

        Action act = enumerateFiles
            ? () => _ = FileSystemPathSafety.GetDirectoryFilesWithNoReparsePoints(linkedRoot, PathSubject)
            : () => FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(linkedRoot, PathSubject);

        act.Should().Throw<InvalidDataException>()
            .WithMessage(LinkedMessage);
    }

    [Fact]
    public void EnsureDirectoryTreeHasNoReparsePoints_ChildReparsePoint_RejectsPath()
    {
        using TestDirectory directory = new();
        string rootPath = Path.Combine(directory.Path, "Root");
        string linkedTarget = Path.Combine(directory.Path, "Target");
        string linkPath = Path.Combine(rootPath, "Linked");
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(linkedTarget);
        ReparsePointTestSupport.CreateDirectoryJunction(linkPath, linkedTarget);

        Action act = () => FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(rootPath, PathSubject);

        act.Should().Throw<InvalidDataException>()
            .WithMessage(LinkedMessage);
    }

    [Fact]
    public void EnsureDirectoryTreeHasNoReparsePoints_NestedReparsePoint_RejectsPath()
    {
        using TestDirectory directory = new();
        string rootPath = Path.Combine(directory.Path, "Root");
        string nestedPath = Path.Combine(rootPath, "Nested");
        string targetPath = Path.Combine(directory.Path, "Target");
        string linkPath = Path.Combine(nestedPath, "Linked");
        Directory.CreateDirectory(nestedPath);
        ReparsePointTestSupport.CreateDirectoryJunction(linkPath, targetPath);

        Action act = () => FileSystemPathSafety.EnsureDirectoryTreeHasNoReparsePoints(rootPath, PathSubject);

        act.Should().Throw<InvalidDataException>()
            .WithMessage(LinkedMessage);
    }

    [Fact]
    public void GetDirectoryFilesWithNoReparsePoints_ReturnsNestedFiles()
    {
        using TestDirectory directory = new();
        string firstFilePath = Path.Combine(directory.Path, "first.txt");
        string secondFilePath = Path.Combine(directory.Path, "Nested", "second.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(secondFilePath)!);
        File.WriteAllText(firstFilePath, "first");
        File.WriteAllText(secondFilePath, "second");

        IReadOnlyList<string> result = FileSystemPathSafety.GetDirectoryFilesWithNoReparsePoints(
            directory.Path,
            PathSubject);

        result.Should().BeEquivalentTo(firstFilePath, secondFilePath);
    }

    [Fact]
    public void GetDirectoryFilesWithNoReparsePoints_ChildReparsePoint_RejectsTree()
    {
        using TestDirectory directory = new();
        string rootPath = Path.Combine(directory.Path, "Root");
        string linkedTarget = Path.Combine(directory.Path, "Target");
        Directory.CreateDirectory(rootPath);
        ReparsePointTestSupport.CreateDirectoryJunction(Path.Combine(rootPath, "Linked"), linkedTarget);

        Action act = () => FileSystemPathSafety.GetDirectoryFilesWithNoReparsePoints(rootPath, PathSubject);

        act.Should().Throw<InvalidDataException>()
            .WithMessage(LinkedMessage);
    }

    [Fact]
    public void CreateRecursiveNoLinksOptions_SkipsReparsePoints()
    {
        EnumerationOptions result = FileSystemPathSafety.CreateRecursiveNoLinksOptions();

        result.AttributesToSkip.Should().Be(FileAttributes.ReparsePoint);
        result.IgnoreInaccessible.Should().BeFalse();
        result.RecurseSubdirectories.Should().BeTrue();
        result.ReturnSpecialDirectories.Should().BeFalse();
    }
}
