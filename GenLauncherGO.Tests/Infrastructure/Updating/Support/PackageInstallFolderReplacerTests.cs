using System;
using System.IO;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Infrastructure.Updating.Support;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Support;

public sealed class PackageInstallFolderReplacerTests
{
    [Fact]
    public void ReplaceMovesTemporaryFolderIntoNewInstalledLocation()
    {
        using TestDirectory testDirectory = new();
        string temporaryFolder = Path.Combine(testDirectory.Path, "staging", "Mod", "1.0");
        string installedFolder = Path.Combine(testDirectory.Path, "installed", "Mod", "1.0");
        Directory.CreateDirectory(temporaryFolder);
        File.WriteAllText(Path.Combine(temporaryFolder, "asset.txt"), "new");

        PackageInstallFolderReplacer.Replace(
            Own(testDirectory.Path, temporaryFolder),
            Own(testDirectory.Path, installedFolder),
            GetBackupPath(testDirectory, installedFolder),
            NullLogger.Instance);

        Directory.Exists(temporaryFolder).Should().BeFalse();
        File.ReadAllText(Path.Combine(installedFolder, "asset.txt")).Should().Be("new");
    }

    [Fact]
    public void ReplaceMovesExistingInstallToBackupBeforeReplacingIt()
    {
        using TestDirectory testDirectory = new();
        string temporaryFolder = Path.Combine(testDirectory.Path, "staging", "Mod", "1.0");
        string installedFolder = Path.Combine(testDirectory.Path, "installed", "Mod", "1.0");
        Directory.CreateDirectory(temporaryFolder);
        Directory.CreateDirectory(installedFolder);
        File.WriteAllText(Path.Combine(temporaryFolder, "asset.txt"), "new");
        File.WriteAllText(Path.Combine(installedFolder, "asset.txt"), "old");

        PackageInstallFolderReplacer.Replace(
            Own(testDirectory.Path, temporaryFolder),
            Own(testDirectory.Path, installedFolder),
            GetBackupPath(testDirectory, installedFolder),
            NullLogger.Instance);

        File.ReadAllText(Path.Combine(installedFolder, "asset.txt")).Should().Be("new");
        Directory.Exists(GetBackupPath(testDirectory, installedFolder).FullPath).Should().BeFalse();
    }

    [Fact]
    public void ReplaceThrowsWhenTemporaryFolderDoesNotExist()
    {
        using TestDirectory testDirectory = new();
        string temporaryFolder = Path.Combine(testDirectory.Path, "missing");
        string installedFolder = Path.Combine(testDirectory.Path, "installed");

        Action act = () => PackageInstallFolderReplacer.Replace(
            Own(testDirectory.Path, temporaryFolder),
            Own(testDirectory.Path, installedFolder),
            GetBackupPath(testDirectory, installedFolder),
            NullLogger.Instance);

        act.Should().Throw<DirectoryNotFoundException>()
            .WithMessage("*Temporary package folder*");
    }

    [SymbolicLinkFact]
    public void ReplaceThrowsWhenTemporaryTreeContainsReparsePoint()
    {
        using TestDirectory testDirectory = new();
        string temporaryFolder = Path.Combine(testDirectory.Path, "staging", "Mod", "1.0");
        string installedFolder = Path.Combine(testDirectory.Path, "installed", "Mod", "1.0");
        string linkTarget = Path.Combine(testDirectory.Path, "linked-target");
        string linkPath = Path.Combine(temporaryFolder, "Linked");
        Directory.CreateDirectory(temporaryFolder);
        Directory.CreateDirectory(linkTarget);
        File.WriteAllText(Path.Combine(temporaryFolder, "asset.txt"), "new");
        SymbolicLinkTestSupport.CreateDirectoryLink(linkPath, linkTarget);

        Action act = () => PackageInstallFolderReplacer.Replace(
            Own(testDirectory.Path, temporaryFolder),
            Own(testDirectory.Path, installedFolder),
            GetBackupPath(testDirectory, installedFolder),
            NullLogger.Instance);

        act.Should().Throw<IOException>()
            .WithMessage("*reparse point*");
        Directory.Exists(temporaryFolder).Should().BeTrue();
        Directory.Exists(installedFolder).Should().BeFalse();
    }

    [SymbolicLinkFact]
    public void ReplaceThrowsWhenInstalledPathChainContainsReparsePoint()
    {
        using TestDirectory testDirectory = new();
        string temporaryFolder = Path.Combine(testDirectory.Path, "staging", "Mod", "1.0");
        string realInstalledRoot = Path.Combine(testDirectory.Path, "real-installed");
        string linkedInstalledRoot = Path.Combine(testDirectory.Path, "installed-link");
        string installedFolder = Path.Combine(linkedInstalledRoot, "Mod", "1.0");
        Directory.CreateDirectory(temporaryFolder);
        Directory.CreateDirectory(realInstalledRoot);
        File.WriteAllText(Path.Combine(temporaryFolder, "asset.txt"), "new");
        SymbolicLinkTestSupport.CreateDirectoryLink(linkedInstalledRoot, realInstalledRoot);

        Action act = () => PackageInstallFolderReplacer.Replace(
            Own(testDirectory.Path, temporaryFolder),
            Own(testDirectory.Path, installedFolder),
            GetBackupPath(testDirectory, installedFolder),
            NullLogger.Instance);

        act.Should().Throw<IOException>()
            .WithMessage("*reparse point*");
        Directory.Exists(temporaryFolder).Should().BeTrue();
        File.Exists(Path.Combine(realInstalledRoot, "Mod", "1.0", "asset.txt")).Should().BeFalse();
    }

    [Fact]
    public void ReplaceRestoresExistingInstallWhenReplacementMoveFails()
    {
        using TestDirectory testDirectory = new();
        string installedFolder = Path.Combine(testDirectory.Path, "installed", "Mod", "1.0");
        Directory.CreateDirectory(installedFolder);
        File.WriteAllText(Path.Combine(installedFolder, "asset.txt"), "old");

        Action act = () => PackageInstallFolderReplacer.Replace(
            Own(testDirectory.Path, installedFolder),
            Own(testDirectory.Path, installedFolder),
            GetBackupPath(testDirectory, installedFolder),
            NullLogger.Instance);

        act.Should().Throw<IOException>();
        File.ReadAllText(Path.Combine(installedFolder, "asset.txt")).Should().Be("old");
        Directory.Exists(GetBackupPath(testDirectory, installedFolder).FullPath).Should().BeFalse();
    }

    [Fact]
    public void ReplaceLeavesLegitimateSiblingVersionNamedBackupUntouched()
    {
        using TestDirectory testDirectory = new();
        string temporaryFolder = Path.Combine(testDirectory.Path, "staging", "Mod", "1.0");
        string installedFolder = Path.Combine(testDirectory.Path, "installed", "Mod", "1.0");
        string legitimateSibling = Path.Combine(testDirectory.Path, "installed", "Mod", "1.0.backup");
        Directory.CreateDirectory(temporaryFolder);
        Directory.CreateDirectory(installedFolder);
        Directory.CreateDirectory(legitimateSibling);
        File.WriteAllText(Path.Combine(temporaryFolder, "asset.txt"), "new");
        File.WriteAllText(Path.Combine(installedFolder, "asset.txt"), "old");
        File.WriteAllText(Path.Combine(legitimateSibling, "asset.txt"), "legitimate");

        OwnedContentPath recoveryBackup = GetBackupPath(testDirectory, installedFolder);
        PackageInstallFolderReplacer.Replace(
            Own(testDirectory.Path, temporaryFolder),
            Own(testDirectory.Path, installedFolder),
            recoveryBackup,
            NullLogger.Instance);

        File.ReadAllText(Path.Combine(installedFolder, "asset.txt")).Should().Be("new");
        File.ReadAllText(Path.Combine(legitimateSibling, "asset.txt")).Should().Be("legitimate");
        Directory.Exists(recoveryBackup.FullPath).Should().BeFalse();
    }

    [Fact]
    public void ReplaceRejectsRecoveryPathInsideInstalledContentBeforeMutation()
    {
        using TestDirectory testDirectory = new();
        string temporaryFolder = Path.Combine(testDirectory.Path, "staging", "Mod", "1.0");
        string installedFolder = Path.Combine(testDirectory.Path, "installed", "Mod", "1.0");
        string overlappingBackup = Path.Combine(installedFolder, "recovery");
        Directory.CreateDirectory(temporaryFolder);
        Directory.CreateDirectory(installedFolder);
        File.WriteAllText(Path.Combine(temporaryFolder, "asset.txt"), "new");
        File.WriteAllText(Path.Combine(installedFolder, "asset.txt"), "old");

        Action act = () => PackageInstallFolderReplacer.Replace(
            Own(testDirectory.Path, temporaryFolder),
            Own(testDirectory.Path, installedFolder),
            Own(testDirectory.Path, overlappingBackup),
            NullLogger.Instance);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("backupPath");
        File.ReadAllText(Path.Combine(installedFolder, "asset.txt")).Should().Be("old");
        Directory.Exists(temporaryFolder).Should().BeTrue();
    }

    [Fact]
    public void ReplaceKeepsCommittedInstallAndDurableRecoveryBackupWhenPostCommitCleanupFails()
    {
        using TestDirectory testDirectory = new();
        string temporaryRoot = Path.Combine(testDirectory.Path, "Temp", "Packages");
        string installedRoot = Path.Combine(testDirectory.Path, "Launcher", "Mods");
        string temporaryFolder = Path.Combine(temporaryRoot, "Mod", "1.0");
        string installedFolder = Path.Combine(installedRoot, "Mod", "1.0");
        Directory.CreateDirectory(temporaryFolder);
        Directory.CreateDirectory(installedFolder);
        File.WriteAllText(Path.Combine(temporaryFolder, "asset.txt"), "new");
        File.WriteAllText(Path.Combine(installedFolder, "asset.txt"), "old");
        var temporaryPath = new OwnedContentPath(temporaryRoot, temporaryFolder);
        var installedPath = new OwnedContentPath(installedRoot, installedFolder);
        OwnedContentPath backupPath = GetBackupPath(testDirectory, installedFolder);

        PackageInstallFolderReplacer.Replace(
            temporaryPath,
            installedPath,
            backupPath,
            NullLogger.Instance,
            _ => throw new IOException("cleanup failed"));

        File.ReadAllText(Path.Combine(installedFolder, "asset.txt")).Should().Be("new");
        File.ReadAllText(Path.Combine(backupPath.FullPath, "asset.txt")).Should().Be("old");
    }

    [Fact]
    public void ReplaceRestoresInterruptedBackupBeforeRejectingMissingStagingFolder()
    {
        using TestDirectory testDirectory = new();
        string temporaryFolder = Path.Combine(testDirectory.Path, "staging", "Mod", "1.0");
        string installedFolder = Path.Combine(testDirectory.Path, "installed", "Mod", "1.0");
        OwnedContentPath recoveryBackup = GetBackupPath(testDirectory, installedFolder);
        Directory.CreateDirectory(recoveryBackup.FullPath);
        File.WriteAllText(Path.Combine(recoveryBackup.FullPath, "asset.txt"), "old");

        Action act = () => PackageInstallFolderReplacer.Replace(
            Own(testDirectory.Path, temporaryFolder),
            Own(testDirectory.Path, installedFolder),
            GetBackupPath(testDirectory, installedFolder),
            NullLogger.Instance);

        act.Should().Throw<DirectoryNotFoundException>();
        File.ReadAllText(Path.Combine(installedFolder, "asset.txt")).Should().Be("old");
        Directory.Exists(recoveryBackup.FullPath).Should().BeFalse();
    }

    [Fact]
    public void ReplaceReconcilesCommittedBackupBeforeStartingNextReplacement()
    {
        using TestDirectory testDirectory = new();
        string temporaryFolder = Path.Combine(testDirectory.Path, "staging", "Mod", "1.0");
        string installedFolder = Path.Combine(testDirectory.Path, "installed", "Mod", "1.0");
        OwnedContentPath recoveryBackup = GetBackupPath(testDirectory, installedFolder);
        Directory.CreateDirectory(temporaryFolder);
        Directory.CreateDirectory(installedFolder);
        Directory.CreateDirectory(recoveryBackup.FullPath);
        File.WriteAllText(Path.Combine(temporaryFolder, "asset.txt"), "next");
        File.WriteAllText(Path.Combine(installedFolder, "asset.txt"), "current");
        File.WriteAllText(Path.Combine(recoveryBackup.FullPath, "asset.txt"), "old");

        PackageInstallFolderReplacer.Replace(
            Own(testDirectory.Path, temporaryFolder),
            Own(testDirectory.Path, installedFolder),
            GetBackupPath(testDirectory, installedFolder),
            NullLogger.Instance);

        File.ReadAllText(Path.Combine(installedFolder, "asset.txt")).Should().Be("next");
        Directory.Exists(recoveryBackup.FullPath).Should().BeFalse();
    }

    [SymbolicLinkFact]
    public void ReplaceRejectsLinkedRecoveryBackup()
    {
        using TestDirectory testDirectory = new();
        string temporaryFolder = Path.Combine(testDirectory.Path, "staging", "Mod", "1.0");
        string installedFolder = Path.Combine(testDirectory.Path, "installed", "Mod", "1.0");
        OwnedContentPath recoveryBackup = GetBackupPath(testDirectory, installedFolder);
        string linkTarget = Path.Combine(testDirectory.Path, "outside-backup");
        Directory.CreateDirectory(temporaryFolder);
        Directory.CreateDirectory(installedFolder);
        Directory.CreateDirectory(linkTarget);
        File.WriteAllText(Path.Combine(linkTarget, "asset.txt"), "outside");
        Directory.CreateDirectory(Path.GetDirectoryName(recoveryBackup.FullPath)!);
        SymbolicLinkTestSupport.CreateDirectoryLink(recoveryBackup.FullPath, linkTarget);

        Action act = () => PackageInstallFolderReplacer.Replace(
            Own(testDirectory.Path, temporaryFolder),
            Own(testDirectory.Path, installedFolder),
            GetBackupPath(testDirectory, installedFolder),
            NullLogger.Instance);

        act.Should().Throw<IOException>()
            .WithMessage("*reparse point*");
        File.ReadAllText(Path.Combine(linkTarget, "asset.txt")).Should().Be("outside");
        Directory.Exists(temporaryFolder).Should().BeTrue();
    }

    private static OwnedContentPath Own(string ownerRoot, string fullPath)
    {
        return new OwnedContentPath(ownerRoot, fullPath);
    }

    private static OwnedContentPath GetBackupPath(TestDirectory testDirectory, string installedPath)
    {
        string backupRoot = Path.Combine(testDirectory.Path, "Runtime", "State", "PackageBackups");
        string backupPath = Path.Combine(
            backupRoot,
            Path.GetRelativePath(testDirectory.Path, installedPath));
        return new OwnedContentPath(backupRoot, backupPath);
    }
}
