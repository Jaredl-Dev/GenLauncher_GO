using System;
using System.IO;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Support;

public sealed class PackageInstallFolderReplacerTests
{
    private static readonly string _versionRelativePath = Path.Combine("Mod", "1.0");

    [Fact]
    public void Replace_MovesTemporaryFolderIntoNewInstalledLocation()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        Directory.CreateDirectory(paths.TemporaryPath.FullPath);
        File.WriteAllText(Path.Combine(paths.TemporaryPath.FullPath, "asset.txt"), "new");

        PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            paths.InstalledPath,
            paths.BackupPath,
            NullLogger.Instance);

        Directory.Exists(paths.TemporaryPath.FullPath).Should().BeFalse();
        File.ReadAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt")).Should().Be("new");
        Directory.Exists(paths.BackupPath.OwnerRoot).Should().BeFalse();
    }

    [Fact]
    public void Replace_MovesExistingInstallToBackupBeforeReplacingIt()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        Directory.CreateDirectory(paths.TemporaryPath.FullPath);
        Directory.CreateDirectory(paths.InstalledPath.FullPath);
        File.WriteAllText(Path.Combine(paths.TemporaryPath.FullPath, "asset.txt"), "new");
        File.WriteAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt"), "old");

        PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            paths.InstalledPath,
            paths.BackupPath,
            NullLogger.Instance);

        File.ReadAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt")).Should().Be("new");
        Directory.Exists(paths.BackupPath.FullPath).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(paths.BackupPath.FullPath)!).Should().BeFalse();
        Directory.Exists(paths.BackupPath.OwnerRoot).Should().BeFalse();
    }

    [Fact]
    public void Replace_ThrowsWhenTemporaryFolderDoesNotExist()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);

        Action act = () => PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            paths.InstalledPath,
            paths.BackupPath,
            NullLogger.Instance);

        act.Should().Throw<DirectoryNotFoundException>()
            .WithMessage("*Temporary package folder*");
    }

    [Fact]
    public void Replace_ThrowsWhenTemporaryTreeContainsReparsePoint()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        string linkTarget = testDirectory.CreateDirectory("linked-target");
        Directory.CreateDirectory(paths.TemporaryPath.FullPath);
        File.WriteAllText(Path.Combine(paths.TemporaryPath.FullPath, "asset.txt"), "new");
        ReparsePointTestSupport.CreateDirectoryJunction(
            Path.Combine(paths.TemporaryPath.FullPath, "Linked"),
            linkTarget);

        Action act = () => PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            paths.InstalledPath,
            paths.BackupPath,
            NullLogger.Instance);

        act.Should().Throw<IOException>()
            .WithMessage("*reparse point*");
        Directory.Exists(paths.TemporaryPath.FullPath).Should().BeTrue();
        Directory.Exists(paths.InstalledPath.FullPath).Should().BeFalse();
    }

    [Fact]
    public void Replace_ThrowsWhenInstalledPathChainContainsReparsePoint()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        string realInstalledRoot = testDirectory.CreateDirectory("real-installed");
        string linkedInstalledRoot = Path.Combine(testDirectory.Path, "installed-link");
        var linkedInstalledPath = new OwnedContentPath(
            testDirectory.Path,
            Path.Combine(linkedInstalledRoot, _versionRelativePath));
        Directory.CreateDirectory(paths.TemporaryPath.FullPath);
        File.WriteAllText(Path.Combine(paths.TemporaryPath.FullPath, "asset.txt"), "new");
        ReparsePointTestSupport.CreateDirectoryJunction(linkedInstalledRoot, realInstalledRoot);

        Action act = () => PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            linkedInstalledPath,
            paths.BackupPath,
            NullLogger.Instance);

        act.Should().Throw<IOException>()
            .WithMessage("*reparse point*");
        Directory.Exists(paths.TemporaryPath.FullPath).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(realInstalledRoot).Should().BeEmpty();
    }

    [Fact]
    public void Replace_RestoresExistingInstallWhenReplacementMoveFails()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        Directory.CreateDirectory(paths.InstalledPath.FullPath);
        File.WriteAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt"), "old");

        Action act = () => PackageInstallFolderReplacer.Replace(
            paths.InstalledPath,
            paths.InstalledPath,
            paths.BackupPath,
            NullLogger.Instance);

        act.Should().Throw<IOException>();
        File.ReadAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt")).Should().Be("old");
        Directory.Exists(paths.BackupPath.OwnerRoot).Should().BeFalse();
    }

    [Fact]
    public void Replace_LeavesLegitimateSiblingVersionNamedBackupUntouched()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        string legitimateSibling = paths.InstalledPath.FullPath + ".backup";
        Directory.CreateDirectory(paths.TemporaryPath.FullPath);
        Directory.CreateDirectory(paths.InstalledPath.FullPath);
        Directory.CreateDirectory(legitimateSibling);
        File.WriteAllText(Path.Combine(paths.TemporaryPath.FullPath, "asset.txt"), "new");
        File.WriteAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt"), "old");
        File.WriteAllText(Path.Combine(legitimateSibling, "asset.txt"), "legitimate");

        PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            paths.InstalledPath,
            paths.BackupPath,
            NullLogger.Instance);

        File.ReadAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt")).Should().Be("new");
        File.ReadAllText(Path.Combine(legitimateSibling, "asset.txt")).Should().Be("legitimate");
        Directory.Exists(paths.BackupPath.FullPath).Should().BeFalse();
    }

    /// <summary>
    ///     A recovery backup that overlaps installed or staged content in either direction would delete the very tree it
    ///     exists to restore, so the overlap is rejected before anything on disk is touched.
    /// </summary>
    [Theory]
    [InlineData("BackupInsideInstalled")]
    [InlineData("InstalledInsideBackup")]
    [InlineData("BackupInsideTemporary")]
    [InlineData("TemporaryInsideBackup")]
    public void Replace_OverlappingRecoveryPath_RejectsBeforeMutation(string overlap)
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        Directory.CreateDirectory(paths.TemporaryPath.FullPath);
        Directory.CreateDirectory(paths.InstalledPath.FullPath);
        File.WriteAllText(Path.Combine(paths.TemporaryPath.FullPath, "asset.txt"), "new");
        File.WriteAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt"), "old");
        string overlappingBackup = overlap switch
        {
            "BackupInsideInstalled" => Path.Combine(paths.InstalledPath.FullPath, "recovery"),
            "InstalledInsideBackup" => Path.GetDirectoryName(paths.InstalledPath.FullPath)!,
            "BackupInsideTemporary" => Path.Combine(paths.TemporaryPath.FullPath, "recovery"),
            "TemporaryInsideBackup" => Path.GetDirectoryName(paths.TemporaryPath.FullPath)!,
            _ => throw new ArgumentOutOfRangeException(nameof(overlap), overlap, null)
        };

        Action act = () => PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            paths.InstalledPath,
            new OwnedContentPath(testDirectory.Path, overlappingBackup),
            NullLogger.Instance);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("backupPath");
        File.ReadAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt")).Should().Be("old");
        File.ReadAllText(Path.Combine(paths.TemporaryPath.FullPath, "asset.txt")).Should().Be("new");
    }

    [Fact]
    public void Replace_KeepsCommittedInstallAndDurableRecoveryBackupWhenPostCommitCleanupFails()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        Directory.CreateDirectory(paths.TemporaryPath.FullPath);
        Directory.CreateDirectory(paths.InstalledPath.FullPath);
        File.WriteAllText(Path.Combine(paths.TemporaryPath.FullPath, "asset.txt"), "new");
        File.WriteAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt"), "old");

        PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            paths.InstalledPath,
            paths.BackupPath,
            NullLogger.Instance,
            _ => throw new IOException("cleanup failed"));

        File.ReadAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt")).Should().Be("new");
        File.ReadAllText(Path.Combine(paths.BackupPath.FullPath, "asset.txt")).Should().Be("old");
    }

    /// <summary>
    ///     A recovery backup that outlives its cleanup is the only copy of the previous install, so a replacement that
    ///     cannot remove it must stop rather than overwrite the install it can no longer restore.
    /// </summary>
    [Fact]
    public void Replace_StaleRecoveryBackupSurvivesCleanup_FailsBeforeMutation()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        Directory.CreateDirectory(paths.TemporaryPath.FullPath);
        Directory.CreateDirectory(paths.InstalledPath.FullPath);
        Directory.CreateDirectory(paths.BackupPath.FullPath);
        File.WriteAllText(Path.Combine(paths.TemporaryPath.FullPath, "asset.txt"), "next");
        File.WriteAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt"), "current");
        File.WriteAllText(Path.Combine(paths.BackupPath.FullPath, "asset.txt"), "old");

        Action act = () => PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            paths.InstalledPath,
            paths.BackupPath,
            NullLogger.Instance,
            _ => { });

        act.Should().Throw<IOException>()
            .WithMessage("*stale recovery backup*");
        File.ReadAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt")).Should().Be("current");
        File.ReadAllText(Path.Combine(paths.TemporaryPath.FullPath, "asset.txt")).Should().Be("next");
    }

    [Fact]
    public void Replace_RestoresInterruptedBackupBeforeRejectingMissingStagingFolder()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        Directory.CreateDirectory(paths.BackupPath.FullPath);
        File.WriteAllText(Path.Combine(paths.BackupPath.FullPath, "asset.txt"), "old");

        Action act = () => PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            paths.InstalledPath,
            paths.BackupPath,
            NullLogger.Instance);

        act.Should().Throw<DirectoryNotFoundException>();
        File.ReadAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt")).Should().Be("old");
        Directory.Exists(paths.BackupPath.OwnerRoot).Should().BeFalse();
    }

    [Fact]
    public void Replace_CommittedBackup_ReconcilesBeforeNextReplacement()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        Directory.CreateDirectory(paths.TemporaryPath.FullPath);
        Directory.CreateDirectory(paths.InstalledPath.FullPath);
        Directory.CreateDirectory(paths.BackupPath.FullPath);
        File.WriteAllText(Path.Combine(paths.TemporaryPath.FullPath, "asset.txt"), "next");
        File.WriteAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt"), "current");
        File.WriteAllText(Path.Combine(paths.BackupPath.FullPath, "asset.txt"), "old");

        PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            paths.InstalledPath,
            paths.BackupPath,
            NullLogger.Instance);

        File.ReadAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt")).Should().Be("next");
        Directory.Exists(paths.BackupPath.FullPath).Should().BeFalse();
    }

    [Fact]
    public void Replace_RejectsLinkedRecoveryBackup()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        string linkTarget = testDirectory.CreateDirectory("outside-backup");
        Directory.CreateDirectory(paths.TemporaryPath.FullPath);
        Directory.CreateDirectory(paths.InstalledPath.FullPath);
        File.WriteAllText(Path.Combine(linkTarget, "asset.txt"), "outside");
        Directory.CreateDirectory(Path.GetDirectoryName(paths.BackupPath.FullPath)!);
        ReparsePointTestSupport.CreateDirectoryJunction(paths.BackupPath.FullPath, linkTarget);

        Action act = () => PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            paths.InstalledPath,
            paths.BackupPath,
            NullLogger.Instance);

        act.Should().Throw<IOException>()
            .WithMessage("*reparse point*");
        File.ReadAllText(Path.Combine(linkTarget, "asset.txt")).Should().Be("outside");
        Directory.Exists(paths.TemporaryPath.FullPath).Should().BeTrue();
    }

    /// <summary>
    ///     A durable recovery backup is the only copy of the previous install, so a replacement that discovers a linked
    ///     install folder must stop before the reconciliation step that would discard it.
    /// </summary>
    [Fact]
    public void Replace_LinkedInstalledFolder_RejectsWithoutDiscardingTheRecoveryBackup()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        string outsideInstall = testDirectory.CreateDirectory("outside-install");
        string outsideAssetPath = testDirectory.CreateFile(
            Path.Combine("outside-install", "outside.txt"),
            "outside");
        Directory.CreateDirectory(paths.TemporaryPath.FullPath);
        Directory.CreateDirectory(paths.BackupPath.FullPath);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.InstalledPath.FullPath)!);
        File.WriteAllText(Path.Combine(paths.TemporaryPath.FullPath, "asset.txt"), "next");
        File.WriteAllText(Path.Combine(paths.BackupPath.FullPath, "asset.txt"), "old");
        ReparsePointTestSupport.CreateDirectoryJunction(paths.InstalledPath.FullPath, outsideInstall);

        Action act = () => PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            paths.InstalledPath,
            paths.BackupPath,
            NullLogger.Instance);

        act.Should().Throw<IOException>()
            .WithMessage("*reparse point*");
        File.ReadAllText(Path.Combine(paths.BackupPath.FullPath, "asset.txt")).Should().Be("old");
        File.ReadAllText(outsideAssetPath).Should().Be("outside");
    }

    /// <summary>
    ///     The install folder is validated again inside the staged move, because the recovery-state cleanup that runs
    ///     first gives another process a window to swap the folder for a link the earlier check already cleared.
    /// </summary>
    [Fact]
    public void Replace_InstalledFolderLinkedDuringRecoveryCleanup_RejectsBeforeMovingAnything()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        string outsideInstall = testDirectory.CreateDirectory("outside-install");
        string outsideAssetPath = testDirectory.CreateFile(
            Path.Combine("outside-install", "outside.txt"),
            "outside");
        Directory.CreateDirectory(paths.TemporaryPath.FullPath);
        Directory.CreateDirectory(paths.InstalledPath.FullPath);
        Directory.CreateDirectory(paths.BackupPath.FullPath);
        File.WriteAllText(Path.Combine(paths.TemporaryPath.FullPath, "asset.txt"), "next");
        File.WriteAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt"), "current");
        File.WriteAllText(Path.Combine(paths.BackupPath.FullPath, "asset.txt"), "old");

        Action act = () => PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            paths.InstalledPath,
            paths.BackupPath,
            NullLogger.Instance,
            ownedBackupPath =>
            {
                Directory.Delete(ownedBackupPath.FullPath, true);
                Directory.Delete(paths.InstalledPath.FullPath, true);
                ReparsePointTestSupport.CreateDirectoryJunction(
                    paths.InstalledPath.FullPath,
                    outsideInstall);
            });

        act.Should().Throw<IOException>()
            .WithMessage("*reparse point*");
        File.ReadAllText(outsideAssetPath).Should().Be("outside");
        File.ReadAllText(Path.Combine(paths.TemporaryPath.FullPath, "asset.txt")).Should().Be("next");
    }

    /// <summary>
    ///     Staging reached only by following a linked parent belongs to whatever the link points at, so the replacement
    ///     must refuse it instead of moving that content into the launcher's installed package location.
    /// </summary>
    [Fact]
    public void Replace_TemporaryFolderBehindLinkedParent_RejectsWithoutConsumingTheLinkTarget()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths launcherPaths = TestLauncherPaths.Create(testDirectory);
        PackageUpdatePathSet paths = TestPackageUpdatePaths.Create(
            launcherPaths,
            _versionRelativePath,
            _versionRelativePath);
        string outsideStaging = testDirectory.CreateDirectory("outside-staging");
        string outsideAssetPath = testDirectory.CreateFile(
            Path.Combine("outside-staging", "1.0", "asset.txt"),
            "outside");
        Directory.CreateDirectory(launcherPaths.PackagesDirectory);
        ReparsePointTestSupport.CreateDirectoryJunction(
            Path.GetDirectoryName(paths.TemporaryPath.FullPath)!,
            outsideStaging);

        Action act = () => PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            paths.InstalledPath,
            paths.BackupPath,
            NullLogger.Instance);

        act.Should().Throw<IOException>()
            .WithMessage("*reparse point*");
        File.ReadAllText(outsideAssetPath).Should().Be("outside");
        Directory.Exists(paths.InstalledPath.FullPath).Should().BeFalse();
    }

    /// <summary>
    ///     A first install has no previous version to fall back to, so it creates no recovery backup and must leave the
    ///     recovery root that other packages share exactly as it found it.
    /// </summary>
    [Fact]
    public void Replace_FirstInstall_LeavesTheSharedRecoveryRootUntouched()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        Directory.CreateDirectory(paths.TemporaryPath.FullPath);
        Directory.CreateDirectory(paths.BackupPath.OwnerRoot);
        File.WriteAllText(Path.Combine(paths.TemporaryPath.FullPath, "asset.txt"), "new");

        PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            paths.InstalledPath,
            paths.BackupPath,
            NullLogger.Instance);

        File.ReadAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt")).Should().Be("new");
        Directory.Exists(paths.BackupPath.OwnerRoot).Should().BeTrue();
    }

    /// <summary>
    ///     Removing a committed package's stale recovery backup also prunes the recovery folders it emptied, so the
    ///     launcher's state directory does not accumulate one dead folder per package it has ever updated.
    /// </summary>
    [Fact]
    public void Replace_StaleRecoveryBackupRemoved_PrunesTheEmptiedRecoveryFolders()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        Directory.CreateDirectory(paths.InstalledPath.FullPath);
        Directory.CreateDirectory(paths.BackupPath.FullPath);
        File.WriteAllText(Path.Combine(paths.InstalledPath.FullPath, "asset.txt"), "current");
        File.WriteAllText(Path.Combine(paths.BackupPath.FullPath, "asset.txt"), "old");

        Action act = () => PackageInstallFolderReplacer.Replace(
            paths.TemporaryPath,
            paths.InstalledPath,
            paths.BackupPath,
            NullLogger.Instance);

        act.Should().Throw<DirectoryNotFoundException>();
        Directory.Exists(paths.BackupPath.OwnerRoot).Should().BeFalse();
    }

    private static PackageUpdatePathSet CreatePackagePaths(TestDirectory testDirectory)
    {
        return TestPackageUpdatePaths.Create(
            TestLauncherPaths.Create(testDirectory),
            _versionRelativePath,
            _versionRelativePath);
    }
}
