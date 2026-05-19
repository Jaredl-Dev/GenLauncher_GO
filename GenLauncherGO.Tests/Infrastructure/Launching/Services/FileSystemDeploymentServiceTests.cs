using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Launching.Services;
using GenLauncherGO.Infrastructure.Launching.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Services;

public sealed class FileSystemDeploymentServiceTests
{
    [Fact]
    public void Prepare_UsesHardLinkWhenCreatorSucceeds()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FakeHardLinkCreator hardLinks = new();
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
    public void Prepare_CopiesFileWhenHardLinkCreatorFails()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        DateTime packageWriteTimeUtc = new(2022, 4, 5, 6, 7, 8, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(packageRoot, "Data", "file.ini"), packageWriteTimeUtc);
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        string targetPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        File.ReadAllText(targetPath).Should().Be("mod");
        File.GetLastWriteTimeUtc(targetPath).Should().Be(packageWriteTimeUtc);
        Directory.EnumerateFiles(paths.GameDirectory, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void Prepare_WhenTheCopyStagingPathIsOccupied_FailsWithoutTouchingTheGameFile()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        string? deployStagingPath = null;
        FakeHardLinkCreator hardLinks = new()
        {
            CanCreateHardLinks = false,
            SameVolumeCheck = (_, secondPath) =>
            {
                if (!secondPath.Contains(".GenLauncherGO-deploy-", StringComparison.Ordinal))
                {
                    return;
                }

                deployStagingPath = secondPath;
                Directory.CreateDirectory(Path.GetDirectoryName(secondPath)!);
                File.WriteAllText(secondPath, "squatter");
            }
        };
        FileSystemDeploymentService service = CreateService(hardLinks);

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        deployStagingPath.Should().NotBeNull();
        File.Exists(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().BeFalse();
        Directory.EnumerateFiles(paths.GameDirectory, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
        File.Exists(Path.Combine(paths.DeploymentDirectory, "active.json")).Should().BeFalse();
        File.Exists(Path.Combine(paths.DeploymentDirectory, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void Prepare_WhenTheStagedHardLinkIsNotTheSourceFile_FailsWithoutDeployingIt()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FakeHardLinkCreator hardLinks = new()
        {
            UseRealHardLinks = false,
            CreateHook = (targetPath, _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.WriteAllText(targetPath, "impostor");
            }
        };
        FileSystemDeploymentService service = CreateService(hardLinks);

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.Exists(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().BeFalse();
        Directory.EnumerateFiles(paths.GameDirectory, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
        File.Exists(Path.Combine(paths.DeploymentDirectory, "active.json")).Should().BeFalse();
        File.Exists(Path.Combine(paths.DeploymentDirectory, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void Prepare_OverActiveDeployment_RestoresOriginalsBeforeDeployingNewPackages()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "original");
        string firstPackageRoot = CreatePackage(paths, "First", ("Data/file.ini", "a"));
        string secondPackageRoot = CreatePackage(paths, "Second", ("Data/file.ini", "b"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });
        service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(firstPackageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None).Succeeded.Should().BeTrue();

        DeploymentResult secondPrepareResult = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(secondPackageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        secondPrepareResult.Succeeded.Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("b");

        DeploymentResult cleanupResult = service.Cleanup(paths, CancellationToken.None);

        cleanupResult.Succeeded.Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("original");
        Directory.Exists(Path.Combine(paths.DeploymentDirectory, "Backups")).Should().BeFalse();
    }

    [Fact]
    public void Cleanup_RestoresBackedUpOriginalFile()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        Directory.CreateDirectory(Path.Combine(paths.GameDirectory, "Data"));
        File.WriteAllText(Path.Combine(paths.GameDirectory, "Data", "file.ini"), "original");
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });
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
    public void PrepareAndCleanup_CrossVolumeFallbackRestoresOriginalContentAndMetadata()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "original");
        DateTime originalWriteTimeUtc = new(2012, 3, 4, 5, 6, 8, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(targetPath, originalWriteTimeUtc);
        File.SetAttributes(targetPath, FileAttributes.ReadOnly | FileAttributes.Hidden);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FakeHardLinkCreator hardLinks = new() { PathsOnSameVolume = false };
        FileSystemDeploymentService service = CreateService(hardLinks);

        try
        {
            DeploymentResult prepareResult = service.Prepare(
                paths,
                new[] { CreateDeploymentPackage(packageRoot, 0) },
                Array.Empty<string>(),
                CancellationToken.None);

            prepareResult.Succeeded.Should().BeTrue();
            File.ReadAllText(targetPath).Should().Be("mod");
            hardLinks.CreatedLinks.Should().BeEmpty();

            DeploymentResult cleanupResult = service.Cleanup(paths, CancellationToken.None);

            cleanupResult.Succeeded.Should().BeTrue();
            File.ReadAllText(targetPath).Should().Be("original");
            File.GetLastWriteTimeUtc(targetPath).Should().Be(originalWriteTimeUtc);
            File.GetAttributes(targetPath).Should().HaveFlag(FileAttributes.ReadOnly);
            File.GetAttributes(targetPath).Should().HaveFlag(FileAttributes.Hidden);
            Directory.Exists(Path.Combine(paths.DeploymentDirectory, "Backups")).Should().BeFalse();
            Directory.EnumerateFiles(paths.GameDirectory, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(targetPath))
            {
                File.SetAttributes(targetPath, FileAttributes.Normal);
            }
        }
    }

    [Fact]
    public void Cleanup_CrossVolumeRestoreCollisionHoldsTheBackupBytes_DeletesTheStagingFile()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "original");
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        string? restoreStagingPath = null;
        FakeHardLinkCreator hardLinks = new()
        {
            CanCreateHardLinks = false,
            PathsOnSameVolume = false,
            SameVolumeCheck = (_, secondPath) =>
            {
                if (!secondPath.Contains(".GenLauncherGO-restore-", StringComparison.Ordinal))
                {
                    return;
                }

                restoreStagingPath = secondPath;
                File.WriteAllText(secondPath, "original");
                File.SetAttributes(secondPath, FileAttributes.ReadOnly);
            }
        };
        FileSystemDeploymentService service = CreateService(hardLinks);
        service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None).Succeeded.Should().BeTrue();

        DeploymentResult cleanupResult = service.Cleanup(paths, CancellationToken.None);

        cleanupResult.Succeeded.Should().BeFalse();
        restoreStagingPath.Should().NotBeNull();
        File.Exists(restoreStagingPath!).Should().BeFalse();
        File.Exists(targetPath).Should().BeFalse();
        Directory.EnumerateFiles(
                Path.Combine(paths.DeploymentDirectory, "Backups"),
                "*",
                SearchOption.AllDirectories)
            .Should().ContainSingle();
        File.Exists(Path.Combine(paths.DeploymentDirectory, "active.json")).Should().BeTrue();
        File.Exists(Path.Combine(paths.DeploymentDirectory, "journal.jsonl")).Should().BeTrue();
    }

    [Fact]
    public void Cleanup_CrossVolumeRestoreCollisionHoldsForeignBytes_LeavesTheStagingFileUntouched()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "original");
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        string? restoreStagingPath = null;
        FakeHardLinkCreator hardLinks = new()
        {
            CanCreateHardLinks = false,
            PathsOnSameVolume = false,
            SameVolumeCheck = (_, secondPath) =>
            {
                if (!secondPath.Contains(".GenLauncherGO-restore-", StringComparison.Ordinal))
                {
                    return;
                }

                restoreStagingPath = secondPath;
                File.WriteAllText(secondPath, "foreign");
                File.SetAttributes(secondPath, FileAttributes.ReadOnly);
            }
        };
        FileSystemDeploymentService service = CreateService(hardLinks);
        service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None).Succeeded.Should().BeTrue();

        try
        {
            DeploymentResult cleanupResult = service.Cleanup(paths, CancellationToken.None);

            cleanupResult.Succeeded.Should().BeFalse();
            restoreStagingPath.Should().NotBeNull();
            File.ReadAllText(restoreStagingPath!).Should().Be("foreign");
            File.Exists(targetPath).Should().BeFalse();
            Directory.EnumerateFiles(
                    Path.Combine(paths.DeploymentDirectory, "Backups"),
                    "*",
                    SearchOption.AllDirectories)
                .Should().ContainSingle();
            File.Exists(Path.Combine(paths.DeploymentDirectory, "active.json")).Should().BeTrue();
            File.Exists(Path.Combine(paths.DeploymentDirectory, "journal.jsonl")).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(restoreStagingPath))
            {
                File.SetAttributes(restoreStagingPath!, FileAttributes.Normal);
            }
        }
    }

    [Fact]
    public void Cleanup_CrossVolumeBackupChangesBeforeRestore_FailsClosedAndPreservesChangedBytes()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "original");
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        string? restoreStagingPath = null;
        FakeHardLinkCreator hardLinks = new()
        {
            CanCreateHardLinks = false,
            PathsOnSameVolume = false,
            SameVolumeCheck = (sourcePath, secondPath) =>
            {
                if (!secondPath.Contains(".GenLauncherGO-restore-", StringComparison.Ordinal))
                {
                    return;
                }

                restoreStagingPath = secondPath;
                File.WriteAllText(sourcePath, "tampered");
            }
        };
        FileSystemDeploymentService service = CreateService(hardLinks);
        DeploymentResult prepareResult = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);
        prepareResult.Succeeded.Should().BeTrue();

        DeploymentResult cleanupResult = service.Cleanup(paths, CancellationToken.None);

        cleanupResult.Succeeded.Should().BeFalse();
        restoreStagingPath.Should().NotBeNull();
        File.ReadAllText(restoreStagingPath!).Should().Be("tampered");
        File.Exists(targetPath).Should().BeFalse();
        Directory.EnumerateFiles(
                Path.Combine(paths.DeploymentDirectory, "Backups"),
                "*",
                SearchOption.AllDirectories)
            .Should().ContainSingle(file => File.ReadAllText(file) == "tampered");
        File.Exists(Path.Combine(paths.DeploymentDirectory, "active.json")).Should().BeTrue();
        File.Exists(Path.Combine(paths.DeploymentDirectory, "journal.jsonl")).Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Cleanup_BackupMissingOrCorrupt_FailsClosedAndPreservesRecoveryState(bool deleteBackup)
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "original");
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });
        service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);
        string backupPath = Directory.EnumerateFiles(
                Path.Combine(paths.DeploymentDirectory, "Backups"),
                "*",
                SearchOption.AllDirectories)
            .Single();
        if (deleteBackup)
        {
            File.Delete(backupPath);
        }
        else
        {
            File.WriteAllText(backupPath, "corrupt");
        }

        DeploymentResult result = service.Cleanup(paths, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.ReadAllText(targetPath).Should().Be("mod");
        File.Exists(Path.Combine(paths.DeploymentDirectory, "active.json")).Should().BeTrue();
        File.Exists(Path.Combine(paths.DeploymentDirectory, "journal.jsonl")).Should().BeTrue();
        if (!deleteBackup)
        {
            File.ReadAllText(backupPath).Should().Be("corrupt");
        }
    }

    [Fact]
    public void Cleanup_RestoresEqualContentOriginalInsteadOfLeavingDeployedHardLink()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "same");
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
    public void Cleanup_LeavesReplacementOfDeployedHardLinkUntouchedEvenWhenBytesMatch()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        string targetPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        FileSystemDeploymentService service = CreateService(new WindowsHardLinkCreator());
        DeploymentResult prepareResult = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);
        prepareResult.Succeeded.Should().BeTrue();

        File.Delete(targetPath);
        File.WriteAllText(targetPath, "mod");

        DeploymentResult cleanupResult = service.Cleanup(paths, CancellationToken.None);

        cleanupResult.Succeeded.Should().BeFalse();
        File.ReadAllText(targetPath).Should().Be("mod");
        File.Exists(Path.Combine(paths.DeploymentDirectory, "active.json")).Should().BeTrue();
    }

    [Fact]
    public void Prepare_CopiesReadOnlyPackageFileWithoutChangingPackageAttributes()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        string packageSourcePath = Path.Combine(packageRoot, "Data", "file.ini");
        File.SetAttributes(packageSourcePath, FileAttributes.ReadOnly | FileAttributes.Hidden);
        FakeHardLinkCreator hardLinks = new();
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
    public void Cleanup_LeavesModifiedDeployedFileAndOriginalBackupUntouched()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "original");
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });
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
    public void Cleanup_DoesNotDeleteModifiedDeploymentWithoutOriginalBackup()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });
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
    public void Cleanup_DoesNotDeleteRestoredOriginalWhenManifestWasAlreadyApplied()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "original");
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });
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
    public void Cleanup_RemovesCreatedDirectoriesOnlyWhenEmpty()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/Sub/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });
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
    public void Cleanup_RemovesEveryCreatedDirectoryDeepestFirstWhenNothingWasLeftBehind()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/Sub/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });
        service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        DeploymentResult result = service.Cleanup(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        Directory.Exists(Path.Combine(paths.GameDirectory, "Data", "Sub")).Should().BeFalse();
        Directory.Exists(Path.Combine(paths.GameDirectory, "Data")).Should().BeFalse();
    }

    [Fact]
    public void Prepare_DeploysGibSourceAsBigTarget()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(paths, "Mod", ("PatchData.gib", "archive"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

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
    public void Prepare_LetsHigherPrecedencePackageWinTargetConflict()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string modRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        string addonRoot = CreatePackage(paths, "Addon", ("Data/file.ini", "addon"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Prepare(
            paths,
            new[]
            {
                CreateDeploymentPackage(modRoot, 0),
                CreateDeploymentPackage(addonRoot, 1)
            },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().Be("addon");
    }

    [Fact]
    public void Prepare_DisablesExistingRequestedFilesAndCleanupRestoresThem()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string scriptsDirectory = Path.Combine(paths.GameDirectory, "Data", "Scripts");
        Directory.CreateDirectory(scriptsDirectory);
        string multiplayerScripts = Path.Combine(scriptsDirectory, "MultiplayerScripts.scb");
        string scriptsIni = Path.Combine(scriptsDirectory, "Scripts.ini");
        File.WriteAllText(multiplayerScripts, "multiplayer");
        File.WriteAllText(scriptsIni, "scripts");
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult prepareResult = service.Prepare(
            paths,
            Array.Empty<DeploymentPackage>(),
            new[]
            {
                "Data/Scripts/MultiplayerScripts.scb",
                "Data/Scripts/SkirmishScripts.scb",
                "Data/Scripts/Scripts.ini"
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
    public void Prepare_NormalizesAndDeduplicatesDisabledTargets()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string scriptsDirectory = Path.Combine(paths.GameDirectory, "Data", "Scripts");
        Directory.CreateDirectory(scriptsDirectory);
        string scriptsIni = Path.Combine(scriptsDirectory, "Scripts.ini");
        File.WriteAllText(scriptsIni, "scripts");
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Prepare(
            paths,
            Array.Empty<DeploymentPackage>(),
            new[] { @"Data\Scripts\Scripts.ini", "Data/Scripts/Scripts.ini", " Data/Scripts/Scripts.ini ", " " },
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.Exists(scriptsIni).Should().BeFalse();
    }

    [Fact]
    public void Prepare_ReusesDisabledFileBackupWhenPackageDeploysSameTarget()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string scriptsDirectory = Path.Combine(paths.GameDirectory, "Data", "Scripts");
        Directory.CreateDirectory(scriptsDirectory);
        string scriptsIni = Path.Combine(scriptsDirectory, "Scripts.ini");
        File.WriteAllText(scriptsIni, "original");
        string packageRoot = CreatePackage(paths, "Mod", ("Data/Scripts/Scripts.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

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
    public void Prepare_RecoversPartialDeploymentWhenLaterFileFails()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        Directory.CreateDirectory(Path.Combine(paths.GameDirectory, "A"));
        File.WriteAllText(Path.Combine(paths.GameDirectory, "A", "file.ini"), "original");
        File.WriteAllText(Path.Combine(paths.GameDirectory, "B"), "not-a-directory");
        string packageRoot = CreatePackage(
            paths,
            "Mod",
            ("A/file.ini", "mod"),
            ("B/file.ini", "blocked"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().ContainSingle().Which.Should().Match<DeploymentFailure>(failure =>
            failure.Kind == DeploymentFailureKind.FileSystem &&
            failure.Path == paths.GameDirectory);
        File.ReadAllText(Path.Combine(paths.GameDirectory, "A", "file.ini")).Should().Be("original");
        File.ReadAllText(Path.Combine(paths.GameDirectory, "B")).Should().Be("not-a-directory");
        File.Exists(Path.Combine(paths.DeploymentDirectory, "active.json")).Should().BeFalse();
        File.Exists(Path.Combine(paths.DeploymentDirectory, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void Prepare_DoesNotTranslateCancellationIntoFailure()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });
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
    public void Prepare_CancellationAfterMutation_RecoversPartialDeploymentBeforeRethrowing()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(
            paths,
            "Mod",
            ("A/first.ini", "first"),
            ("B/second.ini", "second"));
        using var cancellationSource = new CancellationTokenSource();
        FileSystemDeploymentService service = CreateService(
            new FakeHardLinkCreator { CanCreateHardLinks = false, CancelOn = cancellationSource });

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
    public void Prepare_CancellationDuringFinalFile_RecoversBeforeRethrowing()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        using var cancellationSource = new CancellationTokenSource();
        FileSystemDeploymentService service = CreateService(
            new FakeHardLinkCreator { CancelOn = cancellationSource });

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
    public void Prepare_FailsWhenDeploymentLockIsHeld()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string deploymentRoot = paths.DeploymentDirectory;
        using FileStream lockStream = new(
            Path.Combine(deploymentRoot, "deployment.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.Exists(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().BeFalse();
    }

    [SymbolicLinkFact]
    public void Prepare_FailsWhenDeploymentLockIsDanglingSymbolicLink()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        SymbolicLinkTestSupport.CreateFileLink(
            Path.Combine(paths.DeploymentDirectory, "deployment.lock"),
            Path.Combine(directory.Path, "missing-lock-target"));
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.Exists(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().BeFalse();
    }

    [SymbolicLinkFact]
    public void Prepare_FailsWhenDeploymentJournalIsDanglingSymbolicLink()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        SymbolicLinkTestSupport.CreateFileLink(
            Path.Combine(paths.DeploymentDirectory, "journal.jsonl"),
            Path.Combine(directory.Path, "missing-journal-target"));
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.Exists(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().BeFalse();
    }

    [Fact]
    public void Prepare_FailsWhenPackageTreeContainsReparsePoint()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = Path.Combine(paths.ModsDirectory, "Mod");
        string linkTarget = Path.Combine(directory.Path, "linked-package-content");
        string linkPath = Path.Combine(packageRoot, "Linked");
        Directory.CreateDirectory(packageRoot);
        Directory.CreateDirectory(linkTarget);
        ReparsePointTestSupport.CreateDirectoryJunction(linkPath, linkTarget);

        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Prepare_FailsWhenGameTargetParentIsReparsePoint()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        string linkedTarget = Path.Combine(directory.Path, "outside-game-data");
        string linkedDataDirectory = Path.Combine(paths.GameDirectory, "Data");
        Directory.CreateDirectory(linkedTarget);
        ReparsePointTestSupport.CreateDirectoryJunction(linkedDataDirectory, linkedTarget);

        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.Exists(Path.Combine(linkedTarget, "file.ini")).Should().BeFalse();
    }

    [Theory]
    [InlineData(DeploymentEntryPoint.Cleanup)]
    [InlineData(DeploymentEntryPoint.Recover)]
    public void CleanupAndRecover_JournalWithoutActiveManifest_RestoreTheBackedUpFile(
        DeploymentEntryPoint entryPoint)
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string deploymentRoot = paths.DeploymentDirectory;
        string backupPath = Path.Combine(deploymentRoot, "Backups", "crash", "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.WriteAllText(backupPath, "original");
        DeploymentJournalWriter.Write(
            paths,
            DeploymentJournalRecord.FileBackedUp(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                DeploymentJournalWriter.FingerprintFrom("original"),
                "Backups/crash/Data/file.ini.partial"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = entryPoint == DeploymentEntryPoint.Cleanup
            ? service.Cleanup(paths, CancellationToken.None)
            : service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().Be("original");
        File.Exists(Path.Combine(deploymentRoot, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void Recover_RestoresBackupStartedFileWhenMoveCompletedBeforeBackedUpJournal()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "original");
        string deploymentRoot = paths.DeploymentDirectory;
        string backupPath = Path.Combine(deploymentRoot, "Backups", "crash", "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        DeploymentJournalWriter.Write(
            paths,
            DeploymentJournalRecord.FileBackupStarted(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                "Backups/crash/Data/file.ini.partial"));
        File.Move(targetPath, backupPath);
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("original");
        File.Exists(backupPath).Should().BeFalse();
        File.Exists(Path.Combine(deploymentRoot, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void Recover_IgnoresBackupStartedRecordWhenBackupWasNotCreated()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "original");
        string deploymentRoot = paths.DeploymentDirectory;
        DeploymentJournalWriter.Write(
            paths,
            DeploymentJournalRecord.FileBackupStarted(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                "Backups/crash/Data/file.ini.partial"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("original");
        File.Exists(Path.Combine(deploymentRoot, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void Recover_RestoresBackupWhenCleanupRestoreStartedAndBackupStillExists()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "mod");
        string deploymentRoot = paths.DeploymentDirectory;
        string backupPath = Path.Combine(deploymentRoot, "Backups", "crash", "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.WriteAllText(backupPath, "original");
        string backupStagingPath = backupPath + ".partial";
        string deployStagingPath = Path.Combine(paths.GameDirectory, "Data", ".file.ini.deploy.tmp");
        string restoreStagingPath = Path.Combine(paths.GameDirectory, "Data", ".file.ini.restore.tmp");
        File.WriteAllText(backupStagingPath, "partial backup");
        File.WriteAllText(deployStagingPath, "incomplete deployment");
        File.WriteAllText(restoreStagingPath, "original");
        DeploymentFileFingerprint originalFingerprint = DeploymentJournalWriter.FingerprintFrom("original");
        DeploymentFileFingerprint modFingerprint = DeploymentJournalWriter.FingerprintFrom("mod");
        DeploymentJournalWriter.Write(
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
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("original");
        File.Exists(backupPath).Should().BeFalse();
        File.Exists(backupStagingPath).Should().BeFalse();
        File.Exists(deployStagingPath).Should().BeFalse();
        File.Exists(restoreStagingPath).Should().BeFalse();
        File.Exists(Path.Combine(deploymentRoot, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void Recover_TreatsMissingBackupAfterCleanupRestoreStartedAsRestored()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "original");
        string deploymentRoot = paths.DeploymentDirectory;
        File.WriteAllText(Path.Combine(deploymentRoot, "active.json"), "{not-json");
        DeploymentFileFingerprint originalFingerprint = DeploymentJournalWriter.FingerprintFrom("original");
        DeploymentJournalWriter.Write(
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
                DeploymentJournalWriter.FingerprintFrom("mod"),
                originalFingerprint,
                "Data/.file.ini.deploy.tmp"),
            DeploymentJournalRecord.FileCleanupRestoreStarted(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                "Data/.file.ini.restore.tmp"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("original");
        File.Exists(Path.Combine(deploymentRoot, "active.json")).Should().BeFalse();
        File.Exists(Path.Combine(deploymentRoot, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void Recover_KeepsNoBackupFileDeletedWhenCleanupDeleteCompleted()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        string deploymentRoot = paths.DeploymentDirectory;
        File.WriteAllText(Path.Combine(deploymentRoot, "active.json"), "{not-json");
        DeploymentJournalWriter.Write(
            paths,
            DeploymentJournalRecord.FileDeployed(
                "Data/file.ini",
                DeploymentMethod.Copy,
                null,
                DeploymentJournalWriter.FingerprintFrom("mod"),
                null,
                "Data/.file.ini.deploy.tmp"),
            DeploymentJournalRecord.FileCleanupDeleted("Data/file.ini"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.Exists(targetPath).Should().BeFalse();
        File.Exists(Path.Combine(deploymentRoot, "active.json")).Should().BeFalse();
        File.Exists(Path.Combine(deploymentRoot, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void Recover_SkipsTruncatedTrailingJournalRecordAndReplaysPriorDurableState()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string deploymentRoot = paths.DeploymentDirectory;
        string backupPath = Path.Combine(deploymentRoot, "Backups", "crash", "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.WriteAllText(backupPath, "original");
        DeploymentJournalWriter.Write(
            paths,
            DeploymentJournalRecord.FileBackedUp(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                DeploymentJournalWriter.FingerprintFrom("original"),
                "Backups/crash/Data/file.ini.partial"));
        string journalPath = Path.Combine(deploymentRoot, "journal.jsonl");
        File.AppendAllText(journalPath, "{\"action\":\"file-deployed\"");
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().Be("original");
        File.Exists(backupPath).Should().BeFalse();
        File.Exists(journalPath).Should().BeFalse();
    }

    [Fact]
    public void Recover_WhenManifestAndJournalAreUnreadable_FailsClosedAndPreservesState()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "user data");
        string manifestPath = Path.Combine(paths.DeploymentDirectory, "active.json");
        string journalPath = Path.Combine(paths.DeploymentDirectory, "journal.jsonl");
        File.WriteAllText(manifestPath, "{not-json");
        File.WriteAllText(journalPath, "{also-not-json");
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().ContainSingle(failure => failure.Kind == DeploymentFailureKind.Manifest);
        File.ReadAllText(targetPath).Should().Be("user data");
        File.Exists(manifestPath).Should().BeTrue();
        File.Exists(journalPath).Should().BeTrue();
    }

    [Fact]
    public void Recover_EmptyJournal_SucceedsAndDeletesJournal()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "user data");
        string journalPath = Path.Combine(paths.DeploymentDirectory, "journal.jsonl");
        File.WriteAllText(journalPath, string.Empty);
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("user data");
        File.Exists(journalPath).Should().BeFalse();
    }

    [Fact]
    public void Recover_NullManifest_FailsAndPreservesManifest()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "user data");
        string manifestPath = Path.Combine(paths.DeploymentDirectory, "active.json");
        File.WriteAllText(manifestPath, "null");
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.ReadAllText(targetPath).Should().Be("user data");
        File.Exists(manifestPath).Should().BeTrue();
    }

    [Theory]
    [InlineData("Backups/crash/Data/file.ini.partial", true)]
    [InlineData("", false)]
    public void Recover_BackupStartedWithoutCompletedBackup_CleansRecordedPartialStagingFile(
        string stagingRelativePath,
        bool createStagingFile)
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "original");
        string stagingPath = Path.Combine(paths.DeploymentDirectory, "Backups", "crash", "Data", "file.ini.partial");
        if (createStagingFile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
            File.WriteAllText(stagingPath, "incomplete backup");
            File.SetAttributes(stagingPath, File.GetAttributes(stagingPath) | FileAttributes.ReadOnly);
        }

        DeploymentJournalWriter.Write(
            paths,
            DeploymentJournalRecord.FileBackupStarted(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                stagingRelativePath));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("original");
        File.Exists(stagingPath).Should().BeFalse();
        File.Exists(Path.Combine(paths.DeploymentDirectory, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void Recover_AcceptsSchemaTwoManifestWithRetiredReportingFields()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });
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
    public void Recover_AcceptsRetiredJournalFieldsWhenActiveManifestIsCorrupt()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string deployedPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(deployedPath)!);
        File.WriteAllText(deployedPath, "mod");
        string deploymentRoot = paths.DeploymentDirectory;
        File.WriteAllText(Path.Combine(deploymentRoot, "active.json"), "{not-json");
        DeploymentJournalWriter.Write(
            paths,
            DeploymentJournalRecord.FileDeployed(
                "Data/file.ini",
                DeploymentMethod.Copy,
                null,
                DeploymentJournalWriter.FingerprintFrom("mod"),
                null,
                "Data/.file.ini.deploy.tmp"));
        string journalPath = Path.Combine(deploymentRoot, "journal.jsonl");
        string[] journalLines = File.ReadAllLines(journalPath);
        JsonObject deployedRecord = JsonNode.Parse(journalLines[1])!.AsObject();
        deployedRecord["sourcePath"] = "source";
        deployedRecord["packageId"] = "Mod";
        journalLines[1] = deployedRecord.ToJsonString();
        File.WriteAllLines(journalPath, journalLines);
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.Exists(deployedPath).Should().BeFalse();
        File.Exists(Path.Combine(deploymentRoot, "active.json")).Should().BeFalse();
        File.Exists(journalPath).Should().BeFalse();
    }

    [Fact]
    public void Recover_RemovesFileFromStartedJournalRecord()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string deployedPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(deployedPath)!);
        File.WriteAllText(deployedPath, "mod");
        string deploymentRoot = paths.DeploymentDirectory;
        DeploymentJournalWriter.Write(
            paths,
            DeploymentJournalRecord.FileDeploymentStarted(
                "Data/file.ini",
                null,
                DeploymentJournalWriter.FingerprintFrom("mod"),
                null,
                "Data/.file.ini.deploy.tmp"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.Exists(deployedPath).Should().BeFalse();
        File.Exists(Path.Combine(deploymentRoot, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void Recover_StartedJournalRecordForReadOnlyLink_KeepsThePackageFileReadOnly()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        string packageSourcePath = Path.Combine(packageRoot, "Data", "file.ini");
        string deployedPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(deployedPath)!);
        new WindowsHardLinkCreator().TryCreateHardLink(deployedPath, packageSourcePath).Should().BeTrue();
        File.SetAttributes(packageSourcePath, FileAttributes.ReadOnly);
        DeploymentJournalWriter.Write(
            paths,
            DeploymentJournalRecord.FileDeploymentStarted(
                "Data/file.ini",
                null,
                DeploymentJournalWriter.FingerprintFrom("mod"),
                null,
                "Data/.file.ini.deploy.tmp"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        try
        {
            DeploymentResult result = service.Recover(paths, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            File.GetAttributes(packageSourcePath).Should().HaveFlag(FileAttributes.ReadOnly);
            File.ReadAllText(packageSourcePath).Should().Be("mod");
        }
        finally
        {
            File.SetAttributes(packageSourcePath, FileAttributes.Normal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recover_ReplaysDirectoryCreationIntent(bool directoryWasCreated)
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string createdDirectoryPath = Path.Combine(paths.GameDirectory, "Data", "Sub");
        if (directoryWasCreated)
        {
            Directory.CreateDirectory(createdDirectoryPath);
        }

        DeploymentJournalWriter.Write(paths, DeploymentJournalRecord.DirectoryCreated("Data/Sub"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        Directory.Exists(createdDirectoryPath).Should().BeFalse();
        File.Exists(Path.Combine(paths.DeploymentDirectory, "journal.jsonl")).Should().BeFalse();
    }

    [Fact]
    public void Recover_RefusesJournalBoundToDifferentPhysicalGameDirectory()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "mod");
        string otherGameDirectory = Path.Combine(directory.Path, "OtherGame");
        Directory.CreateDirectory(otherGameDirectory);
        DeploymentJournalRecord[] records =
        [
            DeploymentJournalRecord.DeploymentStarted(
                "crash",
                PhysicalDirectoryPath.ResolveExisting(otherGameDirectory),
                DeploymentStateStore.GetGameRootIdentity(otherGameDirectory),
                SupportedGame.Generals),
            DeploymentJournalRecord.FileDeployed(
                "Data/file.ini",
                DeploymentMethod.Copy,
                null,
                DeploymentJournalWriter.FingerprintFrom("mod"),
                null,
                "Data/.file.ini.deploy.tmp")
        ];
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        File.WriteAllLines(
            Path.Combine(paths.DeploymentDirectory, "journal.jsonl"),
            records.Select(record => JsonSerializer.Serialize(record, serializerOptions)));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.ReadAllText(targetPath).Should().Be("mod");
        File.Exists(Path.Combine(paths.DeploymentDirectory, "journal.jsonl")).Should().BeTrue();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("crash", true)]
    public void CreatePaths_DeploymentIdShape_SelectsExpectedBackupOwnership(
        string? deploymentId,
        bool expectDeploymentSubdirectory)
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string backupRoot = Path.Combine(paths.DeploymentDirectory, DeploymentStateStore.BackupsDirectoryName);
        string expectedBackupPath = expectDeploymentSubdirectory
            ? Path.Combine(backupRoot, deploymentId!)
            : backupRoot;

        DeploymentStatePaths statePaths = DeploymentStateStore.CreatePaths(paths, deploymentId!);

        statePaths.BackupDirectory.Should().Be(expectedBackupPath);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("schema-older")]
    [InlineData("game")]
    [InlineData("game-root")]
    [InlineData("game-root-identity")]
    public void Recover_StateBindingMismatch_FailsBeforeMutatingGameFile(string mismatch)
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "mod");
        string gameRoot = PhysicalDirectoryPath.ResolveExisting(paths.GameDirectory);
        string gameRootIdentity = DeploymentStateStore.GetGameRootIdentity(paths.GameDirectory);
        var manifest = new DeploymentManifestDocument(
            DeploymentStateStore.CurrentSchemaVersion,
            "active",
            [
                new DeploymentFileDocument(
                    "Data/file.ini",
                    DeploymentMethod.Copy,
                    null,
                    DeploymentJournalWriter.FingerprintFrom("mod"))
            ],
            Array.Empty<string>(),
            gameRoot,
            gameRootIdentity,
            paths.Game);
        string otherGameRoot = directory.CreateDirectory("OtherGame");
        manifest = mismatch switch
        {
            "schema" => manifest with { SchemaVersion = DeploymentStateStore.CurrentSchemaVersion + 1 },
            "schema-older" => manifest with { SchemaVersion = DeploymentStateStore.CurrentSchemaVersion - 1 },
            "game" => manifest with { Game = SupportedGame.ZeroHour },
            "game-root" => manifest with { GameRoot = PhysicalDirectoryPath.ResolveExisting(otherGameRoot) },
            "game-root-identity" => manifest with { GameRootIdentity = "00000000:0000000000000000" },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch), mismatch, null)
        };
        DeploymentStatePaths statePaths = DeploymentStateStore.CreatePaths(paths, string.Empty);
        DeploymentStateStore.WriteManifest(statePaths.ActiveManifestPath, manifest);
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.ReadAllText(targetPath).Should().Be("mod");
        File.Exists(statePaths.ActiveManifestPath).Should().BeTrue();
    }

    [SymbolicLinkFact]
    public void Recover_LinkedJournal_RejectsBeforeReplayingExternalState()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "mod");
        DeploymentJournalWriter.Write(
            paths,
            DeploymentJournalRecord.FileDeployed(
                "Data/file.ini",
                DeploymentMethod.Copy,
                null,
                DeploymentJournalWriter.FingerprintFrom("mod"),
                null,
                "Data/.file.ini.deploy.tmp"));
        string journalPath = Path.Combine(paths.DeploymentDirectory, "journal.jsonl");
        string externalJournalPath = directory.GetPath("external-journal.jsonl");
        File.Move(journalPath, externalJournalPath);
        SymbolicLinkTestSupport.CreateFileLink(journalPath, externalJournalPath);
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.ReadAllText(targetPath).Should().Be("mod");
        File.Exists(externalJournalPath).Should().BeTrue();
    }

    [SymbolicLinkFact]
    public void Recover_LinkedManifest_RejectsBeforeFallingBackToJournal()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "mod");
        DeploymentJournalWriter.Write(
            paths,
            DeploymentJournalRecord.FileDeployed(
                "Data/file.ini",
                DeploymentMethod.Copy,
                null,
                DeploymentJournalWriter.FingerprintFrom("mod"),
                null,
                "Data/.file.ini.deploy.tmp"));
        string manifestPath = Path.Combine(paths.DeploymentDirectory, "active.json");
        string externalManifestPath = directory.CreateFile("external-manifest.json", "null");
        SymbolicLinkTestSupport.CreateFileLink(manifestPath, externalManifestPath);
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.ReadAllText(targetPath).Should().Be("mod");
        File.ReadAllText(externalManifestPath).Should().Be("null");
    }

    [Fact]
    public void Recover_RefusesActiveManifestForDifferentGameRootWhenJournalIsEmpty()
    {
        using var directory = new TestDirectory();
        LauncherPaths originalPaths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(originalPaths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });
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

    [Fact]
    public void Prepare_WhenTheDeploymentStateDirectoryIsMissing_CreatesItAndDeploys()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        Directory.Delete(paths.DeploymentDirectory, true);
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().Be("mod");
    }

    [Fact]
    public void Prepare_WhenTheTargetAppearsWhileStaging_FailsWithoutOverwritingIt()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        string targetPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        FakeHardLinkCreator hardLinks = new()
        {
            CreateHook = (_, _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.WriteAllText(targetPath, "written by the game");
            }
        };
        FileSystemDeploymentService service = CreateService(hardLinks);

        DeploymentResult result = service.Prepare(
            paths,
            new[] { CreateDeploymentPackage(packageRoot, 0) },
            Array.Empty<string>(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.ReadAllText(targetPath).Should().Be("written by the game");
    }

    [Fact]
    public void Cleanup_RestoresTheOriginalOverAnUntouchedDeployedHardLink()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "original");
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new WindowsHardLinkCreator());
        service.Prepare(
                paths,
                new[] { CreateDeploymentPackage(packageRoot, 0) },
                Array.Empty<string>(),
                CancellationToken.None)
            .Succeeded.Should().BeTrue();

        DeploymentResult result = service.Cleanup(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("original");
    }

    [Fact]
    public void Cleanup_WhenTheDeployedHardLinkWasReplacedWithMatchingBytes_KeepsTheBackedUpOriginal()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "original");
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new WindowsHardLinkCreator());
        service.Prepare(
                paths,
                new[] { CreateDeploymentPackage(packageRoot, 0) },
                Array.Empty<string>(),
                CancellationToken.None)
            .Succeeded.Should().BeTrue();
        File.Delete(targetPath);
        File.WriteAllText(targetPath, "mod");

        DeploymentResult result = service.Cleanup(paths, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.ReadAllText(targetPath).Should().Be("mod");
        File.Exists(Path.Combine(paths.DeploymentDirectory, "active.json")).Should().BeTrue();
    }

    [Fact]
    public void Cleanup_WhenTheDeployedCopyAlreadyHoldsTheOriginalBytes_RestoresTheBackup()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "original");
        string packageRoot = CreatePackage(paths, "Mod", ("Data/file.ini", "mod"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });
        service.Prepare(
                paths,
                new[] { CreateDeploymentPackage(packageRoot, 0) },
                Array.Empty<string>(),
                CancellationToken.None)
            .Succeeded.Should().BeTrue();
        File.WriteAllText(targetPath, "original");

        DeploymentResult result = service.Cleanup(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("original");
        Directory.Exists(Path.Combine(paths.DeploymentDirectory, "Backups")).Should().BeFalse();
    }

    [Fact]
    public void Recover_StartedDeploymentOverABackedUpFile_RestoresTheOriginal()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "mod");
        string backupPath = Path.Combine(paths.DeploymentDirectory, "Backups", "crash", "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.WriteAllText(backupPath, "original");
        DeploymentFileFingerprint originalFingerprint = DeploymentJournalWriter.FingerprintFrom("original");
        DeploymentJournalWriter.Write(
            paths,
            DeploymentJournalRecord.FileBackedUp(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                originalFingerprint,
                string.Empty),
            DeploymentJournalRecord.FileDeploymentStarted(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                DeploymentJournalWriter.FingerprintFrom("mod"),
                originalFingerprint,
                "Data/.file.ini.deploy.tmp"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("original");
    }

    [Fact]
    public void Recover_StartedDeploymentWhoseTargetWasModified_LeavesTheGameFileUntouched()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "user edited");
        DeploymentJournalWriter.Write(
            paths,
            DeploymentJournalRecord.FileDeploymentStarted(
                "Data/file.ini",
                null,
                DeploymentJournalWriter.FingerprintFrom("mod"),
                null,
                "Data/.file.ini.deploy.tmp"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        DeploymentResult result = service.Recover(paths, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.ReadAllText(targetPath).Should().Be("user edited");
    }

    [Fact]
    public void Recover_RemovesAReadOnlyRestoreStagingFileLeftByAnInterruptedCleanup()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory, SupportedGame.Generals);
        string targetPath = CreateExistingGameFile(paths, "mod");
        string backupPath = Path.Combine(paths.DeploymentDirectory, "Backups", "crash", "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.WriteAllText(backupPath, "original");
        string restoreStagingPath = Path.Combine(paths.GameDirectory, "Data", ".file.ini.restore.tmp");
        File.WriteAllText(restoreStagingPath, "original");
        File.SetAttributes(restoreStagingPath, FileAttributes.ReadOnly);
        DeploymentFileFingerprint originalFingerprint = DeploymentJournalWriter.FingerprintFrom("original");
        DeploymentJournalWriter.Write(
            paths,
            DeploymentJournalRecord.FileBackedUp(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                originalFingerprint,
                string.Empty),
            DeploymentJournalRecord.FileDeployed(
                "Data/file.ini",
                DeploymentMethod.Copy,
                "Backups/crash/Data/file.ini",
                DeploymentJournalWriter.FingerprintFrom("mod"),
                originalFingerprint,
                "Data/.file.ini.deploy.tmp"),
            DeploymentJournalRecord.FileCleanupRestoreStarted(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                "Data/.file.ini.restore.tmp"));
        FileSystemDeploymentService service = CreateService(new FakeHardLinkCreator { CanCreateHardLinks = false });

        try
        {
            DeploymentResult result = service.Recover(paths, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            File.Exists(restoreStagingPath).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(restoreStagingPath))
            {
                File.SetAttributes(restoreStagingPath, FileAttributes.Normal);
            }
        }
    }

    /// <summary>
    ///     Writes the file the deployment tests target into the game folder, standing in for content a user
    ///     already has there before launch preparation runs.
    /// </summary>
    private static string CreateExistingGameFile(LauncherPaths paths, string contents)
    {
        string targetPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, contents);
        return targetPath;
    }

    private static FileSystemDeploymentService CreateService(IHardLinkCreator hardLinkCreator)
    {
        return new FileSystemDeploymentService(
            hardLinkCreator,
            NullLogger<FileSystemDeploymentService>.Instance);
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

    private static DeploymentPackage CreateDeploymentPackage(
        string root,
        int precedence)
    {
        return new DeploymentPackage(root, precedence);
    }

    /// <summary>
    ///     The two entry points that replay the same persisted deployment state.
    /// </summary>
    public enum DeploymentEntryPoint
    {
        Cleanup,
        Recover
    }
}
