using System;
using System.IO;
using System.Linq;
using System.Threading;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Support;

public sealed class PackageStagingFolderCleanerTests
{
    [Fact]
    public void ClearDirectory_CreatesStagingFolderAndDeletesExistingChildren()
    {
        using TestDirectory testDirectory = new();
        string stagingFolder = Path.Combine(testDirectory.Path, "Packages", "Mod", "1.0");
        string childFolder = Path.Combine(stagingFolder, "Data");
        Directory.CreateDirectory(childFolder);
        File.WriteAllText(Path.Combine(stagingFolder, "stale.txt"), "stale");
        File.WriteAllText(Path.Combine(childFolder, "nested.txt"), "nested");

        PackageStagingFolderCleaner.ClearDirectory(
            OwnPackagePath(testDirectory.Path, stagingFolder),
            NullLogger.Instance);

        Directory.Exists(stagingFolder).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(stagingFolder).Should().BeEmpty();
    }

    [Fact]
    public void DeleteEmptyPackageParents_RemovesEmptyChainThroughPackagesFolder()
    {
        using TestDirectory testDirectory = new();
        string stagingFolder = Path.Combine(testDirectory.Path, "Packages", "Mod", "1.0");
        string packageFolder = Path.GetDirectoryName(stagingFolder)!;
        string packagesFolder = Path.GetDirectoryName(packageFolder)!;
        Directory.CreateDirectory(packageFolder);

        PackageStagingFolderCleaner.DeleteEmptyPackageParents(
            OwnPackagePath(testDirectory.Path, stagingFolder),
            NullLogger.Instance);

        Directory.Exists(packageFolder).Should().BeFalse();
        Directory.Exists(packagesFolder).Should().BeFalse();
        Directory.Exists(testDirectory.Path).Should().BeTrue();
    }

    [Fact]
    public void DeleteEmptyPackageParents_StopsWhenParentContainsOtherEntries()
    {
        using TestDirectory testDirectory = new();
        string stagingFolder = Path.Combine(testDirectory.Path, "Packages", "Mod", "1.0");
        string packageFolder = Path.GetDirectoryName(stagingFolder)!;
        string packagesFolder = Path.GetDirectoryName(packageFolder)!;
        Directory.CreateDirectory(packageFolder);
        File.WriteAllText(Path.Combine(packagesFolder, "keep.txt"), "keep");

        PackageStagingFolderCleaner.DeleteEmptyPackageParents(
            OwnPackagePath(testDirectory.Path, stagingFolder),
            NullLogger.Instance);

        Directory.Exists(packageFolder).Should().BeFalse();
        Directory.Exists(packagesFolder).Should().BeTrue();
        File.Exists(Path.Combine(packagesFolder, "keep.txt")).Should().BeTrue();
    }

    [Fact]
    public void DeleteEmptyPackageParents_ReturnsWhenPathIsNotUnderPackagesFolder()
    {
        using TestDirectory testDirectory = new();
        string stagingFolder = Path.Combine(testDirectory.Path, "Mod", "1.0");
        string packageFolder = Path.GetDirectoryName(stagingFolder)!;
        Directory.CreateDirectory(packageFolder);

        PackageStagingFolderCleaner.DeleteEmptyPackageParents(
            new OwnedContentPath(testDirectory.Path, stagingFolder),
            NullLogger.Instance);

        Directory.Exists(packageFolder).Should().BeTrue();
    }

    [Fact]
    public void DeleteEmptyPackageParents_ReturnsWhenPackagesAncestorDoesNotExist()
    {
        using TestDirectory testDirectory = new();
        string stagingFolder = Path.Combine(testDirectory.Path, "Packages", "Mod", "1.0");

        Action act = () => PackageStagingFolderCleaner.DeleteEmptyPackageParents(
            OwnPackagePath(testDirectory.Path, stagingFolder),
            NullLogger.Instance);

        act.Should().NotThrow();
        Directory.Exists(Path.Combine(testDirectory.Path, "Packages")).Should().BeFalse();
    }

    [Fact]
    public void RemoveUnsafeLinks_HonorsPreCanceledToken()
    {
        using TestDirectory testDirectory = new();
        string stagingFolder = Path.Combine(testDirectory.Path, "Packages", "Mod", "1.0");
        Directory.CreateDirectory(stagingFolder);
        File.WriteAllText(Path.Combine(stagingFolder, "file.txt"), "file");
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();

        Action act = () => PackageStagingFolderCleaner.RemoveUnsafeLinks(
            OwnPackagePath(testDirectory.Path, stagingFolder),
            NullLogger.Instance,
            cancellationTokenSource.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void RemoveUnsafeLinks_RecursesThroughOrdinaryDirectories()
    {
        using TestDirectory testDirectory = new();
        string stagingFolder = Path.Combine(testDirectory.Path, "Packages", "Mod", "1.0");
        string nestedFolder = Path.Combine(stagingFolder, "Data");
        Directory.CreateDirectory(nestedFolder);
        string filePath = Path.Combine(nestedFolder, "file.txt");
        File.WriteAllText(filePath, "file");

        PackageStagingFolderCleaner.RemoveUnsafeLinks(
            OwnPackagePath(testDirectory.Path, stagingFolder),
            NullLogger.Instance,
            CancellationToken.None);

        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public void PruneToManifest_DeletesStaleFilesAndKeepsConvertedBigFiles()
    {
        using TestDirectory testDirectory = new();
        string stagingFolder = Path.Combine(testDirectory.Path, "Packages", "Mod", "1.0");
        string nestedFolder = Path.Combine(stagingFolder, "Data");
        string emptyFolder = Path.Combine(stagingFolder, "Empty");
        Directory.CreateDirectory(nestedFolder);
        Directory.CreateDirectory(Path.Combine(emptyFolder, "Inner", "Deepest"));
        File.WriteAllText(Path.Combine(stagingFolder, "keep.txt"), "keep");
        File.WriteAllText(Path.Combine(stagingFolder, "stale.txt"), "stale");
        File.WriteAllText(Path.Combine(nestedFolder, "asset.gib"), "asset");
        File.WriteAllText(Path.Combine(nestedFolder, "old.txt"), "old");
        RemoteFileManifestEntry[] files =
        [
            new("keep.txt", "hash", 4),
            new("Data/asset.big", "hash", 5)
        ];

        PackageStagingFolderCleaner.PruneToManifest(
            OwnPackagePath(testDirectory.Path, stagingFolder),
            files,
            NullLogger.Instance,
            CancellationToken.None);

        File.Exists(Path.Combine(stagingFolder, "keep.txt")).Should().BeTrue();
        File.Exists(Path.Combine(nestedFolder, "asset.gib")).Should().BeTrue();
        File.Exists(Path.Combine(stagingFolder, "stale.txt")).Should().BeFalse();
        File.Exists(Path.Combine(nestedFolder, "old.txt")).Should().BeFalse();
        Directory.Exists(emptyFolder).Should().BeFalse();
        Directory.EnumerateFiles(stagingFolder, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Should()
            .BeEquivalentTo("keep.txt", "asset.gib");
    }

    [Fact]
    public void PruneToManifest_HonorsPreCanceledToken()
    {
        using TestDirectory testDirectory = new();
        string stagingFolder = Path.Combine(testDirectory.Path, "Packages", "Mod", "1.0");
        Directory.CreateDirectory(stagingFolder);
        File.WriteAllText(Path.Combine(stagingFolder, "keep.txt"), "keep");
        File.WriteAllText(Path.Combine(stagingFolder, "stale.txt"), "stale");
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();

        Action act = () => PackageStagingFolderCleaner.PruneToManifest(
            OwnPackagePath(testDirectory.Path, stagingFolder),
            [new RemoteFileManifestEntry("keep.txt", "hash", 4)],
            NullLogger.Instance,
            cancellationTokenSource.Token);

        act.Should().Throw<OperationCanceledException>();
        File.Exists(Path.Combine(stagingFolder, "keep.txt")).Should().BeTrue();
        File.Exists(Path.Combine(stagingFolder, "stale.txt")).Should().BeTrue();
    }

    private static OwnedContentPath OwnPackagePath(string root, string fullPath)
    {
        return new OwnedContentPath(Path.Combine(root, "Packages"), fullPath);
    }
}
