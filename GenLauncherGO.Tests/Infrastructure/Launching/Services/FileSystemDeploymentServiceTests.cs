using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Launching.Services;
using GenLauncherGO.Infrastructure.Launching.Support;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Services;

public sealed class FileSystemDeploymentServiceTests
{
    [Fact]
    public void PrepareAsyncUsesHardLinkWhenCreatorSucceedsAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FakeHardLinkCreator hardLinks = new(canCreate: true);
        FileSystemDeploymentService service = CreateService(hardLinks);

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().Be("mod");
        hardLinks.CreatedLinks.Should().ContainSingle();
        hardLinks.CreatedLinks[0].TargetPath.Should().NotBe(Path.Combine(paths.GameDirectory, "Data", "file.ini"));
        File.Exists(hardLinks.CreatedLinks[0].TargetPath).Should().BeFalse();
    }

    [Fact]
    public void PrepareAsyncCopiesFileWhenHardLinkCreatorFailsAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().Be("mod");
        Directory.EnumerateFiles(paths.GameDirectory, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void CleanupAsyncRestoresBackedUpOriginalFileAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        Directory.CreateDirectory(Path.Combine(paths.GameDirectory, "Data"));
        File.WriteAllText(Path.Combine(paths.GameDirectory, "Data", "file.ini"), "original");
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));
        service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        DeploymentResult result = service.Cleanup(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().Be("original");
        Directory.Exists(Path.Combine(paths.DeploymentDirectory, "Backups")).Should().BeFalse();
    }

    [Fact]
    public void CleanupRestoresEqualContentOriginalInsteadOfLeavingDeployedHardLink()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string targetPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, "same");
        DateTime originalWriteTimeUtc = new(2012, 3, 4, 5, 6, 8, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(targetPath, originalWriteTimeUtc);
        File.SetAttributes(targetPath, FileAttributes.ReadOnly | FileAttributes.Hidden);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "same"));
        string packageSourcePath = Path.Combine(packageRoot, "Data", "file.ini");
        File.SetLastWriteTimeUtc(
            packageSourcePath,
            new DateTime(2022, 4, 5, 6, 7, 8, DateTimeKind.Utc));
        FileSystemDeploymentService service = CreateService(new WindowsHardLinkCreator());

        try
        {
            DeploymentResult prepareResult = service.Prepare(
                paths,
                new[] { CreateDeploymentPackage(packageRoot, 0) },
                Array.Empty<string>(),
                CancellationToken.None);
            prepareResult.Succeeded.Should().BeTrue();
            DeploymentResult cleanupResult = service.Cleanup(paths, CancellationToken.None);

            cleanupResult.Succeeded.Should().BeTrue();
            File.GetLastWriteTimeUtc(targetPath).Should().Be(originalWriteTimeUtc);
            File.GetAttributes(targetPath).Should().HaveFlag(FileAttributes.ReadOnly);
            File.GetAttributes(targetPath).Should().HaveFlag(FileAttributes.Hidden);
            File.SetAttributes(packageSourcePath, FileAttributes.Normal);
            File.WriteAllText(packageSourcePath, "package changed");
            File.ReadAllText(targetPath).Should().Be("same");
        }
        finally
        {
            if (File.Exists(targetPath))
            {
                File.SetAttributes(targetPath, FileAttributes.Normal);
            }

            if (File.Exists(packageSourcePath))
            {
                File.SetAttributes(packageSourcePath, FileAttributes.Normal);
            }
        }
    }

    [Fact]
    public void PrepareCopiesReadOnlyPackageFileWithoutChangingPackageAttributes()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        string packageSourcePath = Path.Combine(packageRoot, "Data", "file.ini");
        File.SetAttributes(packageSourcePath, FileAttributes.ReadOnly | FileAttributes.Hidden);
        FakeHardLinkCreator hardLinks = new(canCreate: true);
        FileSystemDeploymentService service = CreateService(hardLinks);
        string targetPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");

        try
        {
            DeploymentResult prepareResult = service.Prepare(
                paths,
                new[] { CreateDeploymentPackage(packageRoot, 0) },
                Array.Empty<string>(),
                CancellationToken.None);
            prepareResult.Succeeded.Should().BeTrue();
            hardLinks.CreatedLinks.Should().BeEmpty();

            DeploymentResult cleanupResult = service.Cleanup(paths, CancellationToken.None);

            cleanupResult.Succeeded.Should().BeTrue();
            File.Exists(targetPath).Should().BeFalse();
            File.GetAttributes(packageSourcePath).Should().HaveFlag(FileAttributes.ReadOnly);
            File.GetAttributes(packageSourcePath).Should().HaveFlag(FileAttributes.Hidden);
        }
        finally
        {
            if (File.Exists(packageSourcePath))
            {
                File.SetAttributes(packageSourcePath, FileAttributes.Normal);
            }
        }
    }

    [Fact]
    public void CleanupLeavesModifiedDeployedFileAndOriginalBackupUntouched()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string targetPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, "original");
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));
        service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);
        File.WriteAllText(targetPath, "user-change");

        DeploymentResult result = service.Cleanup(paths, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.ReadAllText(targetPath).Should().Be("user-change");
        Directory.EnumerateFiles(
                Path.Combine(paths.DeploymentDirectory, "Backups"),
                "*",
                SearchOption.AllDirectories)
            .Should()
            .ContainSingle(path => File.ReadAllText(path) == "original");
        File.Exists(Path.Combine(paths.DeploymentDirectory, "active.json")).Should().BeTrue();
    }

    [Fact]
    public void CleanupDoesNotDeleteModifiedDeploymentWithoutOriginalBackup()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));
        service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);
        string targetPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        File.WriteAllText(targetPath, "user-change");

        DeploymentResult result = service.Cleanup(paths, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.ReadAllText(targetPath).Should().Be("user-change");
        File.Exists(Path.Combine(paths.DeploymentDirectory, "active.json")).Should().BeTrue();
    }

    [Fact]
    public void CleanupAsyncDoesNotDeleteRestoredOriginalWhenManifestWasAlreadyAppliedAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string targetPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, "original");
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));
        service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);
        string activeManifestPath = Path.Combine(paths.DeploymentDirectory, "active.json");
        string staleManifest = File.ReadAllText(activeManifestPath);
        service.Cleanup(paths, CancellationToken.None);
        File.WriteAllText(activeManifestPath, staleManifest);

        DeploymentResult result = service.Cleanup(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("original");
    }

    [Fact]
    public void CleanupAsyncRemovesCreatedDirectoriesOnlyWhenEmptyAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/Sub/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));
        service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);
        File.WriteAllText(Path.Combine(paths.GameDirectory, "Data", "keep.txt"), "user");

        DeploymentResult result = service.Cleanup(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        Directory.Exists(Path.Combine(paths.GameDirectory, "Data", "Sub")).Should().BeFalse();
        Directory.Exists(Path.Combine(paths.GameDirectory, "Data")).Should().BeTrue();
        File.ReadAllText(Path.Combine(paths.GameDirectory, "Data", "keep.txt")).Should().Be("user");
    }

    [Fact]
    public void PrepareAsyncDeploysGibSourceAsBigTargetAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string packageRoot = CreatePackage(paths, "Mod", ("PatchData.gib", "archive"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.Exists(Path.Combine(paths.GameDirectory, "PatchData.big")).Should().BeTrue();
        File.Exists(Path.Combine(paths.GameDirectory, "PatchData.gib")).Should().BeFalse();
    }

    [Fact]
    public void PrepareAsyncLetsHigherPrecedencePackageWinTargetConflictAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string modRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        string addonRoot = CreatePackage(paths, "Addon", ("Data/file.ini", "addon"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Prepare(
            paths,
            new[]
            {
                CreateDeploymentPackage(modRoot, 0),
                CreateDeploymentPackage(addonRoot, 1),
            },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().Be("addon");
    }

    [Fact]
    public void PrepareAsyncDisablesExistingRequestedFilesAndCleanupRestoresThemAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string scriptsDirectory = Path.Combine(paths.GameDirectory, "Data", "Scripts");
        Directory.CreateDirectory(scriptsDirectory);
        string multiplayerScripts = Path.Combine(scriptsDirectory, "MultiplayerScripts.scb");
        string scriptsIni = Path.Combine(scriptsDirectory, "Scripts.ini");
        File.WriteAllText(multiplayerScripts, "multiplayer");
        File.WriteAllText(scriptsIni, "scripts");
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult prepareResult = service.Prepare(
            paths,
            Array.Empty<DeploymentPackage>(),
            new[]
            {
                "Data/Scripts/MultiplayerScripts.scb",
                "Data/Scripts/SkirmishScripts.scb",
                "Data/Scripts/Scripts.ini",
            },
            CancellationToken.None);

        prepareResult.Succeeded.Should().BeTrue();
        File.Exists(multiplayerScripts).Should().BeFalse();
        File.Exists(Path.Combine(scriptsDirectory, "SkirmishScripts.scb")).Should().BeFalse();
        File.Exists(scriptsIni).Should().BeFalse();

        DeploymentResult cleanupResult = service.Cleanup(paths, CancellationToken.None);

        cleanupResult.Succeeded.Should().BeTrue();
        File.ReadAllText(multiplayerScripts).Should().Be("multiplayer");
        File.ReadAllText(scriptsIni).Should().Be("scripts");
    }

    [Fact]
    public void PrepareNormalizesAndDeduplicatesDisabledTargets()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string scriptsDirectory = Path.Combine(paths.GameDirectory, "Data", "Scripts");
        Directory.CreateDirectory(scriptsDirectory);
        string scriptsIni = Path.Combine(scriptsDirectory, "Scripts.ini");
        File.WriteAllText(scriptsIni, "scripts");
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Prepare(
            paths,
            Array.Empty<DeploymentPackage>(),
            new[] { @"Data\Scripts\Scripts.ini", "Data/Scripts/Scripts.ini", " " },
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.Exists(scriptsIni).Should().BeFalse();
    }

    [Fact]
    public void PrepareAsyncReusesDisabledFileBackupWhenPackageDeploysSameTargetAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string scriptsDirectory = Path.Combine(paths.GameDirectory, "Data", "Scripts");
        Directory.CreateDirectory(scriptsDirectory);
        string scriptsIni = Path.Combine(scriptsDirectory, "Scripts.ini");
        File.WriteAllText(scriptsIni, "original");
        string packageRoot = CreatePackage(paths, "Mod", ("Data/Scripts/Scripts.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult prepareResult = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            new[] { "Data/Scripts/Scripts.ini" },
            CancellationToken.None);

        prepareResult.Succeeded.Should().BeTrue();
        File.ReadAllText(scriptsIni).Should().Be("mod");

        DeploymentResult cleanupResult = service.Cleanup(paths, CancellationToken.None);

        cleanupResult.Succeeded.Should().BeTrue();
        File.ReadAllText(scriptsIni).Should().Be("original");
    }

    [Fact]
    public void PrepareAsyncRecoversPartialDeploymentWhenLaterFileFailsAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        Directory.CreateDirectory(Path.Combine(paths.GameDirectory, "A"));
        File.WriteAllText(Path.Combine(paths.GameDirectory, "A", "file.ini"), "original");
        File.WriteAllText(Path.Combine(paths.GameDirectory, "B"), "not-a-directory");
        string packageRoot = CreatePackage(
            paths,
            "Mod",
            ("A/file.ini", "mod"),
            ("B/file.ini", "blocked"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.ReadAllText(Path.Combine(paths.GameDirectory, "A", "file.ini")).Should().Be("original");
        File.ReadAllText(Path.Combine(paths.GameDirectory, "B")).Should().Be("not-a-directory");
        File.Exists(Path.Combine(paths.DeploymentDirectory, "active.json")).Should().BeFalse();
        File.Exists(Path.Combine(paths.DeploymentDirectory, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void PrepareDoesNotTranslateCancellationIntoFailure()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        Action act = () => service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            cancellationSource.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void PrepareCancellationAfterMutationRecoversPartialDeploymentBeforeRethrowing()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string packageRoot = CreatePackage(
            paths,
            "Mod",
            ("A/first.ini", "first"),
            ("B/second.ini", "second"));
        using var cancellationSource = new CancellationTokenSource();
        FileSystemDeploymentService service = CreateService(
            new CancelingHardLinkCreator(cancellationSource));

        Action act = () => service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            cancellationSource.Token);

        act.Should().Throw<OperationCanceledException>();
        File.Exists(Path.Combine(paths.GameDirectory, "A", "first.ini")).Should().BeFalse();
        File.Exists(Path.Combine(paths.GameDirectory, "B", "second.ini")).Should().BeFalse();
        File.Exists(Path.Combine(paths.DeploymentDirectory, "active.json")).Should().BeFalse();
        File.Exists(Path.Combine(paths.DeploymentDirectory, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void PrepareCancellationDuringFinalFileRecoversBeforeRethrowing()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        using var cancellationSource = new CancellationTokenSource();
        FileSystemDeploymentService service = CreateService(
            new CancelingWindowsHardLinkCreator(cancellationSource));

        Action act = () => service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            cancellationSource.Token);

        act.Should().Throw<OperationCanceledException>();
        File.Exists(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().BeFalse();
        File.Exists(Path.Combine(paths.DeploymentDirectory, "active.json")).Should().BeFalse();
        File.Exists(Path.Combine(paths.DeploymentDirectory, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void PrepareAsyncFailsWhenDeploymentLockIsHeldAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string deploymentRoot = paths.DeploymentDirectory;
        Directory.CreateDirectory(deploymentRoot);
        using FileStream lockStream = new(
            Path.Combine(deploymentRoot, "deployment.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.Exists(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().BeFalse();
    }

    [SymbolicLinkFact]
    public void PrepareFailsWhenDeploymentLockIsDanglingSymbolicLink()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        Directory.CreateDirectory(paths.DeploymentDirectory);
        SymbolicLinkTestSupport.CreateFileLink(
            Path.Combine(paths.DeploymentDirectory, "deployment.lock"),
            Path.Combine(directory.Path, "missing-lock-target"));
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.Exists(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().BeFalse();
    }

    [SymbolicLinkFact]
    public void PrepareFailsWhenDeploymentJournalIsDanglingSymbolicLink()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        Directory.CreateDirectory(paths.DeploymentDirectory);
        SymbolicLinkTestSupport.CreateFileLink(
            Path.Combine(paths.DeploymentDirectory, "journal.jsonl"),
            Path.Combine(directory.Path, "missing-journal-target"));
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.Exists(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().BeFalse();
    }

    [SymbolicLinkFact]
    public void PrepareAsyncFailsWhenPackageTreeContainsReparsePointAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string packageRoot = Path.Combine(paths.ModsDirectory, "Mod");
        string linkTarget = Path.Combine(directory.Path, "linked-package-content");
        string linkPath = Path.Combine(packageRoot, "Linked");
        Directory.CreateDirectory(packageRoot);
        Directory.CreateDirectory(linkTarget);
        SymbolicLinkTestSupport.CreateDirectoryLink(linkPath, linkTarget);

        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    [SymbolicLinkFact]
    public void PrepareAsyncFailsWhenGameTargetParentIsReparsePointAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        string linkedTarget = Path.Combine(directory.Path, "outside-game-data");
        string linkedDataDirectory = Path.Combine(paths.GameDirectory, "Data");
        Directory.CreateDirectory(linkedTarget);
        SymbolicLinkTestSupport.CreateDirectoryLink(linkedDataDirectory, linkedTarget);

        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.Exists(Path.Combine(linkedTarget, "file.ini")).Should().BeFalse();
    }

    [Fact]
    public void CleanupAsyncRestoresBackedUpFileFromJournalWithoutActiveManifestAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string deploymentRoot = paths.DeploymentDirectory;
        string backupPath = Path.Combine(deploymentRoot, "Backups", "crash", "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.WriteAllText(backupPath, "original");
        WriteJournal(
            paths,
            DeploymentJournalRecord.FileBackedUp(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                CreateFingerprint("original"),
                "Backups/crash/Data/file.ini.partial"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Cleanup(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().Be("original");
        File.Exists(Path.Combine(deploymentRoot, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void RecoverAsyncRestoresBackupStartedFileWhenMoveCompletedBeforeBackedUpJournalAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string targetPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, "original");
        string deploymentRoot = paths.DeploymentDirectory;
        string backupPath = Path.Combine(deploymentRoot, "Backups", "crash", "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        WriteJournal(
            paths,
            DeploymentJournalRecord.FileBackupStarted(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                "Backups/crash/Data/file.ini.partial"));
        File.Move(targetPath, backupPath);
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("original");
        File.Exists(backupPath).Should().BeFalse();
        File.Exists(Path.Combine(deploymentRoot, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void RecoverAsyncIgnoresBackupStartedRecordWhenBackupWasNotCreatedAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string targetPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, "original");
        string deploymentRoot = paths.DeploymentDirectory;
        Directory.CreateDirectory(deploymentRoot);
        WriteJournal(
            paths,
            DeploymentJournalRecord.FileBackupStarted(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                "Backups/crash/Data/file.ini.partial"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("original");
        File.Exists(Path.Combine(deploymentRoot, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void RecoverAsyncRestoresBackupWhenCleanupRestoreStartedAndBackupStillExistsAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string targetPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, "mod");
        string deploymentRoot = paths.DeploymentDirectory;
        string backupPath = Path.Combine(deploymentRoot, "Backups", "crash", "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.WriteAllText(backupPath, "original");
        DeploymentFileFingerprint originalFingerprint = CreateFingerprint("original");
        DeploymentFileFingerprint modFingerprint = CreateFingerprint("mod");
        WriteJournal(
            paths,
            DeploymentJournalRecord.FileBackedUp(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                originalFingerprint,
                "Backups/crash/Data/file.ini.partial"),
            DeploymentJournalRecord.FileDeployed(
                "Data/file.ini",
                DeploymentMethod.Copy,
                "Backups/crash/Data/file.ini",
                modFingerprint,
                originalFingerprint,
                "Data/.file.ini.deploy.tmp"),
            DeploymentJournalRecord.FileCleanupRestoreStarted(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                "Data/.file.ini.restore.tmp"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("original");
        File.Exists(backupPath).Should().BeFalse();
        File.Exists(Path.Combine(deploymentRoot, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void RecoverAsyncTreatsMissingBackupAfterCleanupRestoreStartedAsRestoredAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string targetPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, "original");
        string deploymentRoot = paths.DeploymentDirectory;
        Directory.CreateDirectory(deploymentRoot);
        File.WriteAllText(Path.Combine(deploymentRoot, "active.json"), "{not-json");
        DeploymentFileFingerprint originalFingerprint = CreateFingerprint("original");
        WriteJournal(
            paths,
            DeploymentJournalRecord.FileBackedUp(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                originalFingerprint,
                "Backups/crash/Data/file.ini.partial"),
            DeploymentJournalRecord.FileDeployed(
                "Data/file.ini",
                DeploymentMethod.Copy,
                "Backups/crash/Data/file.ini",
                CreateFingerprint("mod"),
                originalFingerprint,
                "Data/.file.ini.deploy.tmp"),
            DeploymentJournalRecord.FileCleanupRestoreStarted(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                "Data/.file.ini.restore.tmp"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("original");
        File.Exists(Path.Combine(deploymentRoot, "active.json")).Should().BeFalse();
        File.Exists(Path.Combine(deploymentRoot, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void RecoverAsyncKeepsNoBackupFileDeletedWhenCleanupDeleteCompletedAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string targetPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        string deploymentRoot = paths.DeploymentDirectory;
        Directory.CreateDirectory(deploymentRoot);
        File.WriteAllText(Path.Combine(deploymentRoot, "active.json"), "{not-json");
        WriteJournal(
            paths,
            DeploymentJournalRecord.FileDeployed(
                "Data/file.ini",
                DeploymentMethod.Copy,
                backupRelativePath: null,
                deployedFingerprint: CreateFingerprint("mod"),
                backupFingerprint: null,
                stagingRelativePath: "Data/.file.ini.deploy.tmp"),
            DeploymentJournalRecord.FileCleanupDeleted("Data/file.ini"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.Exists(targetPath).Should().BeFalse();
        File.Exists(Path.Combine(deploymentRoot, "active.json")).Should().BeFalse();
        File.Exists(Path.Combine(deploymentRoot, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void RecoverAsyncRestoresBackedUpFileFromJournalWithoutActiveManifestAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string deploymentRoot = paths.DeploymentDirectory;
        string backupPath = Path.Combine(deploymentRoot, "Backups", "crash", "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.WriteAllText(backupPath, "original");
        WriteJournal(
            paths,
            DeploymentJournalRecord.FileBackedUp(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                CreateFingerprint("original"),
                "Backups/crash/Data/file.ini.partial"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().Be("original");
        File.Exists(Path.Combine(deploymentRoot, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void RecoverAcceptsSchemaTwoManifestWithRetiredReportingFields()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));
        service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None).Succeeded.Should().BeTrue();
        string activeManifestPath = Path.Combine(paths.DeploymentDirectory, "active.json");
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(activeManifestPath))!.AsObject();
        manifest["schemaVersion"]!.GetValue<int>().Should().Be(2);
        manifest["createdAtUtc"] = DateTimeOffset.UtcNow;
        JsonObject file = manifest["files"]!.AsArray()[0]!.AsObject();
        file["sourcePath"] = Path.Combine(packageRoot, "Data", "file.ini");
        file["packageId"] = "mod::mod:1.0";
        file["size"] = 3;
        file["lastWriteTimeUtc"] = DateTime.UtcNow;
        File.WriteAllText(activeManifestPath, manifest.ToJsonString());
        File.Delete(Path.Combine(paths.DeploymentDirectory, "journal.jsonl"));

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.Exists(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().BeFalse();
        File.Exists(activeManifestPath).Should().BeFalse();
    }

    [Fact]
    public void RecoverAcceptsRetiredJournalFieldsWhenActiveManifestIsCorrupt()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string deployedPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(deployedPath)!);
        File.WriteAllText(deployedPath, "mod");
        string deploymentRoot = paths.DeploymentDirectory;
        Directory.CreateDirectory(deploymentRoot);
        File.WriteAllText(Path.Combine(deploymentRoot, "active.json"), "{not-json");
        WriteJournal(
            paths,
            DeploymentJournalRecord.FileDeployed(
                "Data/file.ini",
                DeploymentMethod.Copy,
                backupRelativePath: null,
                deployedFingerprint: CreateFingerprint("mod"),
                backupFingerprint: null,
                stagingRelativePath: "Data/.file.ini.deploy.tmp"));
        string journalPath = Path.Combine(deploymentRoot, "journal.jsonl");
        string[] journalLines = File.ReadAllLines(journalPath);
        JsonObject deployedRecord = JsonNode.Parse(journalLines[1])!.AsObject();
        deployedRecord["sourcePath"] = "source";
        deployedRecord["packageId"] = "Mod";
        journalLines[1] = deployedRecord.ToJsonString();
        File.WriteAllLines(journalPath, journalLines);
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.Exists(deployedPath).Should().BeFalse();
        File.Exists(Path.Combine(deploymentRoot, "active.json")).Should().BeFalse();
        File.Exists(journalPath).Should().BeFalse();
    }

    [Fact]
    public void RecoverAsyncRemovesFileFromStartedJournalRecordAsync()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string deployedPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(deployedPath)!);
        File.WriteAllText(deployedPath, "mod");
        string deploymentRoot = paths.DeploymentDirectory;
        Directory.CreateDirectory(deploymentRoot);
        WriteJournal(
            paths,
            DeploymentJournalRecord.FileDeploymentStarted(
                "Data/file.ini",
                backupRelativePath: null,
                deployedFingerprint: CreateFingerprint("mod"),
                backupFingerprint: null,
                stagingRelativePath: "Data/.file.ini.deploy.tmp"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.Exists(deployedPath).Should().BeFalse();
        File.Exists(Path.Combine(deploymentRoot, "journal.jsonl")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecoverSafelyReplaysDirectoryCreationIntent(bool directoryWasCreated)
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string createdDirectoryPath = Path.Combine(paths.GameDirectory, "Data", "Sub");
        if (directoryWasCreated)
        {
            Directory.CreateDirectory(createdDirectoryPath);
        }

        WriteJournal(paths, DeploymentJournalRecord.DirectoryCreated("Data/Sub"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        Directory.Exists(createdDirectoryPath).Should().BeFalse();
        File.Exists(Path.Combine(paths.DeploymentDirectory, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void RecoverRefusesJournalBoundToDifferentPhysicalGameDirectory()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string targetPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, "mod");
        string otherGameDirectory = Path.Combine(directory.Path, "OtherGame");
        Directory.CreateDirectory(otherGameDirectory);
        Directory.CreateDirectory(paths.DeploymentDirectory);
        DeploymentJournalRecord[] records =
        {
            DeploymentJournalRecord.DeploymentStarted(
                "crash",
                PhysicalDirectoryPath.ResolveExisting(otherGameDirectory),
                DeploymentStateStore.GetGameRootIdentity(otherGameDirectory),
                SupportedGame.Generals),
            DeploymentJournalRecord.FileDeployed(
                "Data/file.ini",
                DeploymentMethod.Copy,
                backupRelativePath: null,
                deployedFingerprint: CreateFingerprint("mod"),
                backupFingerprint: null,
                stagingRelativePath: "Data/.file.ini.deploy.tmp"),
        };
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        File.WriteAllLines(
            Path.Combine(paths.DeploymentDirectory, "journal.jsonl"),
            records.Select(record => JsonSerializer.Serialize(record, serializerOptions)));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.ReadAllText(targetPath).Should().Be("mod");
        File.Exists(Path.Combine(paths.DeploymentDirectory, "journal.jsonl")).Should().BeTrue();
    }

    [Fact]
    public void RecoverRefusesActiveManifestForDifferentGameRootWhenJournalIsEmpty()
    {
        using var directory = new TestDirectory();
        LauncherPaths originalPaths = CreatePaths(directory.Path);
        string packageRoot = CreatePackage(originalPaths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator(canCreate: false));
        DeploymentResult prepareResult = service.Prepare(
            originalPaths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);
        prepareResult.Succeeded.Should().BeTrue();
        File.WriteAllText(Path.Combine(originalPaths.DeploymentDirectory, "journal.jsonl"), string.Empty);

        string otherGameDirectory = Path.Combine(directory.Path, "OtherGame");
        string otherTargetPath = Path.Combine(otherGameDirectory, "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(otherTargetPath)!);
        File.WriteAllText(otherTargetPath, "mod");
        LauncherPaths otherPaths = new LauncherStoragePaths(Path.Combine(directory.Path, "Launcher"))
            .CreateGamePaths(SupportedGame.Generals, otherGameDirectory);

        DeploymentResult result = service.Recover(otherPaths, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.ReadAllText(otherTargetPath).Should().Be("mod");
        File.Exists(Path.Combine(originalPaths.DeploymentDirectory, "active.json")).Should().BeTrue();
    }

    private static FileSystemDeploymentService CreateService(IHardLinkCreator hardLinkCreator)
    {
        return new FileSystemDeploymentService(
            hardLinkCreator,
            NullLogger<FileSystemDeploymentService>.Instance);
    }

    private static LauncherPaths CreatePaths(string root)
    {
        string gameDirectory = Path.Combine(root, "Game");
        string executableDirectory = Path.Combine(root, "Launcher");
        Directory.CreateDirectory(gameDirectory);
        Directory.CreateDirectory(executableDirectory);

        LauncherPaths paths = new LauncherStoragePaths(executableDirectory)
            .CreateGamePaths(SupportedGame.Generals, gameDirectory);
        Directory.CreateDirectory(paths.OwnedGameDataDirectory);
        return paths;
    }

    private static string CreatePackage(
        LauncherPaths paths,
        string name,
        params (string RelativePath, string Contents)[] files)
    {
        string packageRoot = Path.Combine(paths.ModsDirectory, name);
        foreach ((string relativePath, string contents) in files)
        {
            string filePath = Path.Combine(packageRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, contents);
        }

        return packageRoot;
    }

    private static void WriteJournal(LauncherPaths paths, params DeploymentJournalRecord[] records)
    {
        Directory.CreateDirectory(paths.DeploymentDirectory);
        var header = DeploymentJournalRecord.DeploymentStarted(
            "crash",
            PhysicalDirectoryPath.ResolveExisting(paths.GameDirectory),
            DeploymentStateStore.GetGameRootIdentity(paths.GameDirectory),
            paths.Game);
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        File.WriteAllLines(
            Path.Combine(paths.DeploymentDirectory, "journal.jsonl"),
            new[] { header }.Concat(records).Select(record => JsonSerializer.Serialize(record, serializerOptions)));
    }

    private static DeploymentFileFingerprint CreateFingerprint(string contents)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(contents);
        return new DeploymentFileFingerprint(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static DeploymentPackage CreateDeploymentPackage(
        string root,
        int precedence)
    {
        return new DeploymentPackage(root, precedence);
    }

    private sealed class FakeHardLinkCreator : IHardLinkCreator
    {
        private readonly bool _canCreate;

        public FakeHardLinkCreator(bool canCreate)
        {
            _canCreate = canCreate;
        }

        public List<(string TargetPath, string SourcePath)> CreatedLinks { get; } = new();

        public bool TryCreateHardLink(string targetPath, string sourcePath)
        {
            if (!_canCreate)
            {
                return false;
            }

            File.Copy(sourcePath, targetPath);
            CreatedLinks.Add((targetPath, sourcePath));
            return true;
        }
    }

    private sealed class CancelingHardLinkCreator : IHardLinkCreator
    {
        private readonly CancellationTokenSource _cancellationSource;

        public CancelingHardLinkCreator(CancellationTokenSource cancellationSource)
        {
            _cancellationSource = cancellationSource;
        }

        public bool TryCreateHardLink(string targetPath, string sourcePath)
        {
            _cancellationSource.Cancel();
            return false;
        }
    }

    private sealed class CancelingWindowsHardLinkCreator : IHardLinkCreator
    {
        private readonly CancellationTokenSource _cancellationSource;

        private readonly WindowsHardLinkCreator _hardLinkCreator = new();

        public CancelingWindowsHardLinkCreator(CancellationTokenSource cancellationSource)
        {
            _cancellationSource = cancellationSource;
        }

        public bool TryCreateHardLink(string targetPath, string sourcePath)
        {
            bool created = _hardLinkCreator.TryCreateHardLink(targetPath, sourcePath);
            _cancellationSource.Cancel();
            return created;
        }
    }
}
