using System;
using System.Collections.Generic;
using System.IO;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Common;

namespace GenLauncherGO.Tests.Infrastructure.Common;

public sealed class OwnedDirectoryTreeTests
{
    [Fact]
    public void EnsureExists_MissingDirectory_CreatesDirectory()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string contentPath = Path.Combine(ownedRoot, "Content");

        string result = OwnedDirectoryTree.EnsureExists(ownedRoot, contentPath);

        result.Should().Be(Path.GetFullPath(contentPath));
        Directory.Exists(contentPath).Should().BeTrue();
    }

    [Fact]
    public void EnsureExists_FileAtDirectoryPath_Throws()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string contentPath = directory.CreateFile("Owned/Content", "occupied");

        Action act = () => OwnedDirectoryTree.EnsureExists(ownedRoot, contentPath);

        act.Should().Throw<IOException>();
        File.ReadAllText(contentPath).Should().Be("occupied");
    }

    [Fact]
    public void EnsureExists_LinkedLeaf_RejectsWithoutTouchingTarget()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string linkPath = Path.Combine(ownedRoot, "Version");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, linkPath);

        Action act = () => OwnedDirectoryTree.EnsureExists(ownedRoot, linkPath);

        act.Should().Throw<InvalidDataException>();
        FileSystemPathSafety.IsReparsePoint(linkPath).Should().BeTrue();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public void EnsureRealDirectory_LinkedLeaf_ReplacesLinkWithoutTouchingTarget()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string linkPath = Path.Combine(ownedRoot, "Version");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, linkPath);

        string result = OwnedDirectoryTree.EnsureRealDirectory(ownedRoot, linkPath);

        result.Should().Be(Path.GetFullPath(linkPath));
        Directory.Exists(linkPath).Should().BeTrue();
        FileSystemPathSafety.IsReparsePoint(linkPath).Should().BeFalse();
        Directory.EnumerateFileSystemEntries(linkPath).Should().BeEmpty();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoryPreparation_FileAtLeaf_RejectsWithoutDeletingFile(bool prepareEmpty)
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string contentPath = directory.CreateFile("Owned/Content", "occupied");

        Action act = prepareEmpty
            ? () => OwnedDirectoryTree.PrepareEmpty(ownedRoot, contentPath)
            : () => OwnedDirectoryTree.EnsureRealDirectory(ownedRoot, contentPath);

        act.Should().Throw<IOException>();
        File.ReadAllText(contentPath).Should().Be("occupied");
    }

    [Fact]
    public void DeleteIfExists_NestedDirectoryLink_DeletesTreeWithoutTouchingTarget()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string contentPath = directory.CreateDirectory("Owned/Content");
        string linkPath = Path.Combine(contentPath, "Linked");
        directory.CreateFile("Owned/Content/owned.txt", "owned");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, linkPath);

        bool deleted = OwnedDirectoryTree.DeleteIfExists(
            new OwnedContentPath(ownedRoot, contentPath));

        deleted.Should().BeTrue();
        Directory.Exists(contentPath).Should().BeFalse();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public void DeleteIfExists_LinkedLeaf_DeletesWithoutTouchingTarget()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string linkPath = Path.Combine(ownedRoot, "Version");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, linkPath);

        bool deleted = OwnedDirectoryTree.DeleteIfExists(
            new OwnedContentPath(ownedRoot, linkPath));

        deleted.Should().BeTrue();
        Directory.Exists(linkPath).Should().BeFalse();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public void DeleteIfExists_LinkedAncestor_RejectsWithoutTouchingTarget()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string linkedAncestor = Path.Combine(ownedRoot, "Linked");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(
            directory,
            linkedAncestor,
            canaryFileName: "Child/target.txt");
        string candidatePath = Path.Combine(linkedAncestor, "Child");

        Action act = () => OwnedDirectoryTree.DeleteIfExists(
            new OwnedContentPath(ownedRoot, candidatePath));

        act.Should().Throw<InvalidDataException>();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public void DeleteIfExists_MissingDirectory_ReturnsFalse()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string missingPath = Path.Combine(ownedRoot, "Missing");

        bool deleted = OwnedDirectoryTree.DeleteIfExists(ownedRoot, missingPath);

        deleted.Should().BeFalse();
        Directory.Exists(ownedRoot).Should().BeTrue();
    }

    [Fact]
    public void PrepareEmpty_PopulatedDirectory_DeletesChildrenWithoutFollowingLinks()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string contentPath = directory.CreateDirectory("Owned/Content");
        string linkPath = Path.Combine(contentPath, "Linked");
        directory.CreateFile("Owned/Content/file.txt", "file");
        directory.CreateFile("Owned/Content/Nested/file.txt", "nested");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, linkPath);

        string result = OwnedDirectoryTree.PrepareEmpty(ownedRoot, contentPath);

        result.Should().Be(Path.GetFullPath(contentPath));
        Directory.Exists(contentPath).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(contentPath).Should().BeEmpty();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public void PrepareEmpty_LinkedLeaf_ReplacesLinkWithoutTouchingTarget()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string linkPath = Path.Combine(ownedRoot, "Scratch");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, linkPath);

        string result = OwnedDirectoryTree.PrepareEmpty(ownedRoot, linkPath);

        result.Should().Be(Path.GetFullPath(linkPath));
        FileSystemPathSafety.IsReparsePoint(linkPath).Should().BeFalse();
        Directory.EnumerateFileSystemEntries(linkPath).Should().BeEmpty();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public void PrepareEmptyExcept_SelectedChild_PreservesOnlySelectedChild()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string contentPath = directory.CreateDirectory("Owned/Content");
        string preservedPath = directory.CreateDirectory("Owned/Content/Preserved");
        string preservedFilePath = directory.CreateFile("Owned/Content/Preserved/state.json", "state");
        string removedPath = directory.CreateDirectory("Owned/Content/Removed");
        directory.CreateFile("Owned/Content/Removed/file.txt", "removed");
        directory.CreateFile("Owned/Content/transient.txt", "transient");

        string result = OwnedDirectoryTree.PrepareEmptyExcept(ownedRoot, contentPath, preservedPath);

        result.Should().Be(Path.GetFullPath(contentPath));
        File.ReadAllText(preservedFilePath).Should().Be("state");
        Directory.Exists(removedPath).Should().BeFalse();
        Directory.EnumerateFileSystemEntries(contentPath).Should().ContainSingle()
            .Which.Should().Be(preservedPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrepareEmptyExcept_MissingOrLinkedRoot_PreparesRealEmptyDirectory(bool linkedRoot)
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string contentPath = Path.Combine(ownedRoot, "Content");
        string? canaryFilePath = null;
        string? canaryContents = null;
        if (linkedRoot)
        {
            ProtectedJunction junction =
                ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, contentPath);
            canaryFilePath = junction.CanaryFilePath;
            canaryContents = junction.CanaryContents;
        }

        string result = OwnedDirectoryTree.PrepareEmptyExcept(
            ownedRoot,
            contentPath,
            Path.Combine(contentPath, "Preserved"));

        result.Should().Be(Path.GetFullPath(contentPath));
        FileSystemPathSafety.IsReparsePoint(contentPath).Should().BeFalse();
        Directory.EnumerateFileSystemEntries(contentPath).Should().BeEmpty();
        if (canaryFilePath is not null)
        {
            File.ReadAllText(canaryFilePath).Should().Be(canaryContents);
        }
    }

    [Fact]
    public void PrepareEmptyExcept_FileAtRoot_RejectsWithoutDeletingFile()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string contentPath = directory.CreateFile("Owned/Content", "occupied");

        Action act = () => OwnedDirectoryTree.PrepareEmptyExcept(
            ownedRoot,
            contentPath,
            Path.Combine(contentPath, "Preserved"));

        act.Should().Throw<IOException>();
        File.ReadAllText(contentPath).Should().Be("occupied");
    }

    [Fact]
    public void DeleteEmptyParents_EmptyAncestorChain_DeletesThroughOwnedBoundary()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string parentPath = directory.CreateDirectory("Owned/A/B");
        string childPath = Path.Combine(parentPath, "missing.file");

        IReadOnlyList<string> deletedPaths = OwnedDirectoryTree.DeleteEmptyParents(ownedRoot, childPath);

        deletedPaths.Should().Equal(
            Path.Combine(ownedRoot, "A", "B"),
            Path.Combine(ownedRoot, "A"));
        Directory.Exists(ownedRoot).Should().BeTrue();
    }

    [Fact]
    public void DeleteEmptyParents_NonEmptyAncestor_StopsAfterEmptyChildParent()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string parentPath = directory.CreateDirectory("Owned/A/B");
        string childPath = Path.Combine(parentPath, "missing.file");
        string siblingFilePath = directory.CreateFile("Owned/A/keep.txt", "keep");

        IReadOnlyList<string> deletedPaths = OwnedDirectoryTree.DeleteEmptyParents(ownedRoot, childPath);

        deletedPaths.Should().Equal(parentPath);
        Directory.Exists(Path.Combine(ownedRoot, "A")).Should().BeTrue();
        File.ReadAllText(siblingFilePath).Should().Be("keep");
    }

    [Fact]
    public void DeleteEmptyParents_ChildOutsideOwnedRoot_Throws()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string siblingPath = directory.CreateDirectory("Sibling/A");
        string childPath = Path.Combine(siblingPath, "missing.file");

        Action act = () => OwnedDirectoryTree.DeleteEmptyParents(ownedRoot, childPath);

        act.Should().Throw<InvalidOperationException>();
        Directory.Exists(siblingPath).Should().BeTrue();
    }

    [Fact]
    public void DeleteEmptyParents_LinkedParent_RejectsWithoutTouchingTarget()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string linkPath = Path.Combine(ownedRoot, "Linked");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, linkPath);
        string childPath = Path.Combine(linkPath, "missing.file");

        Action act = () => OwnedDirectoryTree.DeleteEmptyParents(ownedRoot, childPath);

        act.Should().Throw<InvalidDataException>();
        FileSystemPathSafety.IsReparsePoint(linkPath).Should().BeTrue();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public void DeleteEmptyParents_MissingIntermediate_ContinuesToExistingAncestor()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string existingAncestor = directory.CreateDirectory("Owned/A");
        string childPath = Path.Combine(existingAncestor, "Missing", "Nested", "missing.file");

        IReadOnlyList<string> deletedPaths = OwnedDirectoryTree.DeleteEmptyParents(ownedRoot, childPath);

        deletedPaths.Should().Equal(existingAncestor);
        Directory.Exists(existingAncestor).Should().BeFalse();
        Directory.Exists(ownedRoot).Should().BeTrue();
    }

    [Fact]
    public void DeleteEmptyParentsIncludingRoot_EmptyAncestorChain_DeletesExclusiveOwnedRoot()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string parentPath = directory.CreateDirectory("Owned/A/B");
        string childPath = Path.Combine(parentPath, "missing.file");

        IReadOnlyList<string> deletedPaths = OwnedDirectoryTree.DeleteEmptyParentsIncludingRoot(
            ownedRoot,
            childPath);

        deletedPaths.Should().Equal(
            Path.Combine(ownedRoot, "A", "B"),
            Path.Combine(ownedRoot, "A"),
            ownedRoot);
        Directory.Exists(ownedRoot).Should().BeFalse();
    }

    [Fact]
    public void DeleteEmptyParentsIncludingRoot_NonEmptyRoot_PreservesRoot()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string parentPath = directory.CreateDirectory("Owned/A/B");
        string childPath = Path.Combine(parentPath, "missing.file");
        string retainedFilePath = directory.CreateFile("Owned/retained.txt", "retained");

        IReadOnlyList<string> deletedPaths = OwnedDirectoryTree.DeleteEmptyParentsIncludingRoot(
            ownedRoot,
            childPath);

        deletedPaths.Should().Equal(
            Path.Combine(ownedRoot, "A", "B"),
            Path.Combine(ownedRoot, "A"));
        File.ReadAllText(retainedFilePath).Should().Be("retained");
    }

    [Fact]
    public void DeleteEmptyDirectories_MixedTree_DeletesOnlyEmptyDirectories()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string contentPath = directory.CreateDirectory("Owned/Content");
        string emptyPath = directory.CreateDirectory("Owned/Content/Empty/Nested");
        string retainedFilePath = directory.CreateFile("Owned/Content/Retained/file.txt", "keep");
        string linkPath = Path.Combine(contentPath, "Linked");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, linkPath);
        string targetEmptyPath = directory.CreateDirectory("ExternalTarget/Empty");

        bool rootDeleted = OwnedDirectoryTree.DeleteEmptyDirectories(
            new OwnedContentPath(ownedRoot, contentPath));

        rootDeleted.Should().BeFalse();
        Directory.Exists(emptyPath).Should().BeFalse();
        File.ReadAllText(retainedFilePath).Should().Be("keep");
        FileSystemPathSafety.IsReparsePoint(linkPath).Should().BeTrue();
        Directory.Exists(contentPath).Should().BeTrue();
        Directory.Exists(targetEmptyPath).Should().BeTrue();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public void DeleteEmptyDirectories_EntireTreeIsEmpty_DeletesRoot()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string contentPath = directory.CreateDirectory("Owned/Content/Empty/Nested");
        string treeRoot = Path.Combine(ownedRoot, "Content");

        bool rootDeleted = OwnedDirectoryTree.DeleteEmptyDirectories(
            new OwnedContentPath(ownedRoot, treeRoot));

        rootDeleted.Should().BeTrue();
        Directory.Exists(contentPath).Should().BeFalse();
        Directory.Exists(treeRoot).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DeleteEmptyDirectories_MissingFileOrLinkedRoot_ReturnsFalseWithoutDeletingTarget(
        bool pathExists,
        bool linkedRoot)
    {
        using TestDirectory directory = new();
        (string ownedRoot, string treeRoot, string? protectedFilePath, string? protectedContents) =
            CreateInvalidTreeRoot(directory, pathExists, linkedRoot);

        bool deleted = OwnedDirectoryTree.DeleteEmptyDirectories(
            new OwnedContentPath(ownedRoot, treeRoot));

        deleted.Should().BeFalse();
        if (protectedFilePath is not null)
        {
            File.ReadAllText(protectedFilePath).Should().Be(protectedContents);
        }
    }

    [Fact]
    public void DeleteReparsePoints_NestedLink_DeletesLinkWithoutTouchingTarget()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string contentPath = directory.CreateDirectory("Owned/Content/Nested");
        string treeRoot = Path.Combine(ownedRoot, "Content");
        string linkPath = Path.Combine(contentPath, "Linked");
        string retainedFilePath = directory.CreateFile("Owned/Content/Nested/retained.txt", "retained");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, linkPath);

        IReadOnlyList<string> deletedPaths = OwnedDirectoryTree.DeleteReparsePoints(
            new OwnedContentPath(ownedRoot, treeRoot));

        deletedPaths.Should().Equal(linkPath);
        Directory.Exists(linkPath).Should().BeFalse();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
        File.ReadAllText(retainedFilePath).Should().Be("retained");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DeleteReparsePoints_MissingFileOrLinkedRoot_RejectsWithoutDeletingTarget(
        bool pathExists,
        bool linkedRoot)
    {
        using TestDirectory directory = new();
        (string ownedRoot, string treeRoot, string? protectedFilePath, string? protectedContents) =
            CreateInvalidTreeRoot(directory, pathExists, linkedRoot);

        Action act = () => OwnedDirectoryTree.DeleteReparsePoints(
            new OwnedContentPath(ownedRoot, treeRoot));

        act.Should().Throw<InvalidDataException>();
        if (protectedFilePath is not null)
        {
            File.ReadAllText(protectedFilePath).Should().Be(protectedContents);
        }
    }

    /// <summary>
    ///     A missing leaf is created, so a linked ancestor has to be refused before that: creating the leaf would
    ///     place a launcher directory inside whatever the link resolves to.
    /// </summary>
    [Fact]
    public void EnsureRealDirectory_LinkedParent_RejectsWithoutCreatingThroughTheLink()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string linkPath = Path.Combine(ownedRoot, "Linked");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, linkPath);
        string contentPath = Path.Combine(linkPath, "Content");

        Action act = () => OwnedDirectoryTree.EnsureRealDirectory(ownedRoot, contentPath);

        act.Should().Throw<InvalidDataException>();
        Directory.Exists(Path.Combine(junction.TargetDirectory, "Content")).Should().BeFalse();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    [Fact]
    public void PrepareEmpty_MissingDirectoryUnderLinkedParent_RejectsWithoutCreatingIt()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string linkPath = Path.Combine(ownedRoot, "Linked");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, linkPath);
        string contentPath = Path.Combine(linkPath, "Content");

        Action act = () => OwnedDirectoryTree.PrepareEmpty(ownedRoot, contentPath);

        act.Should().Throw<InvalidDataException>();
        Directory.Exists(Path.Combine(junction.TargetDirectory, "Content")).Should().BeFalse();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    /// <summary>
    ///     A file reached through a link is deleted as a plain file, so the ancestor check is the only thing between
    ///     the delete and a file the launcher does not own.
    /// </summary>
    [Fact]
    public void DeleteIfExists_FileUnderLinkedAncestor_RejectsWithoutDeletingIt()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string linkPath = Path.Combine(ownedRoot, "Linked");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, linkPath);
        string candidatePath = Path.Combine(linkPath, "target.txt");

        Action act = () => OwnedDirectoryTree.DeleteIfExists(ownedRoot, candidatePath);

        act.Should().Throw<InvalidDataException>();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    /// <summary>
    ///     Pruning walks upward and deletes as it goes, so an owned root that is itself a link would hand the walk a
    ///     directory tree that belongs to somebody else.
    /// </summary>
    [Fact]
    public void DeleteEmptyParents_LinkedOwnedRoot_RejectsWithoutDeletingThroughTheLink()
    {
        using TestDirectory directory = new();
        string linkedRoot = directory.GetPath("Owned");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, linkedRoot);
        string emptyParentPath = directory.CreateDirectory("ExternalTarget/A");
        string childPath = Path.Combine(linkedRoot, "A", "missing.file");

        Action act = () => OwnedDirectoryTree.DeleteEmptyParents(linkedRoot, childPath);

        act.Should().Throw<InvalidDataException>();
        Directory.Exists(emptyParentPath).Should().BeTrue();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    /// <summary>
    ///     Pruning is cleanup that runs after a failure, so an unexpected file where a directory was recorded has to
    ///     stop the walk rather than fail the cleanup.
    /// </summary>
    [Fact]
    public void DeleteEmptyParents_FileInAncestorChain_StopsWithoutDeletingIt()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateDirectory("Owned");
        string filePath = directory.CreateFile("Owned/A", "occupied");
        string childPath = Path.Combine(filePath, "missing.file");

        IReadOnlyList<string> deletedPaths = OwnedDirectoryTree.DeleteEmptyParents(ownedRoot, childPath);

        deletedPaths.Should().BeEmpty();
        File.ReadAllText(filePath).Should().Be("occupied");
    }

    [Fact]
    public void DeleteEmptyParentsIncludingRoot_FileAtOwnedRoot_LeavesItUntouched()
    {
        using TestDirectory directory = new();
        string ownedRoot = directory.CreateFile("Owned", "occupied");
        string childPath = Path.Combine(ownedRoot, "missing.file");

        IReadOnlyList<string> deletedPaths = OwnedDirectoryTree.DeleteEmptyParentsIncludingRoot(
            ownedRoot,
            childPath);

        deletedPaths.Should().BeEmpty();
        File.ReadAllText(ownedRoot).Should().Be("occupied");
    }

    /// <summary>
    ///     This overload is handed a launcher-owned root such as the package backup folder. A link in its place has to
    ///     be reported, because answering "nothing was empty" would let the caller believe the folder was inspected.
    /// </summary>
    [Fact]
    public void DeleteEmptyDirectories_LinkedOwnedRoot_RejectsWithoutTouchingTarget()
    {
        using TestDirectory directory = new();
        string linkedRoot = directory.GetPath("Owned");
        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, linkedRoot);
        string emptyChildPath = directory.CreateDirectory("ExternalTarget/Empty");

        Action act = () => OwnedDirectoryTree.DeleteEmptyDirectories(linkedRoot);

        act.Should().Throw<InvalidDataException>();
        Directory.Exists(emptyChildPath).Should().BeTrue();
        junction.ReadCanary().Should().Be(junction.CanaryContents);
    }

    private static (string OwnedRoot, string TreeRoot, string? ProtectedFilePath, string? ProtectedContents)
        CreateInvalidTreeRoot(
            TestDirectory directory,
            bool pathExists,
            bool linkedRoot)
    {
        string ownedRoot = directory.CreateDirectory("Owned");
        string treeRoot = Path.Combine(ownedRoot, "Content");
        if (!pathExists)
        {
            return (ownedRoot, treeRoot, null, null);
        }

        if (!linkedRoot)
        {
            directory.CreateFile("Owned/Content", "occupied");
            return (ownedRoot, treeRoot, treeRoot, "occupied");
        }

        ProtectedJunction junction = ReparsePointTestSupport.CreateJunctionToProtectedTarget(directory, treeRoot);
        return (ownedRoot, treeRoot, junction.CanaryFilePath, junction.CanaryContents);
    }
}
