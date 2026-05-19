using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Mods.Services;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Launching.Services;
using GenLauncherGO.Infrastructure.Launching.Support;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Services;

public sealed class DeploymentLaunchPreparationServiceTests
{
    [Fact]
    public void PrepareResolvesSelectedVersionPathsAndUsesSelectionOrderForPrecedence()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        LauncherContentVersion[] versions =
        [
            CreateVersion(ModificationType.Mod, "Rise", "1.0"),
            CreateVersion(ModificationType.Patch, "Balance", "2.0", "Rise"),
            CreateVersion(ModificationType.Addon, "Maps", "3.0", "Rise"),
        ];
        WriteVersionFile(paths, versions[0], "Data/file.ini", "mod");
        WriteVersionFile(paths, versions[1], "Data/file.ini", "patch");
        WriteVersionFile(paths, versions[2], "Data/file.ini", "addon");
        DeploymentLaunchPreparationService service = CreateService();

        bool succeeded = service.Prepare(
            new LaunchPreparationRequest(
                paths,
                versions,
                disableBaseGameScriptFiles: false),
            CancellationToken.None);

        succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().Be("addon");
    }

    [Fact]
    public void PrepareDisablesBaseGameScriptsAndCleanupRestoresThem()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        LauncherContentVersion version = CreateVersion(ModificationType.Mod, "Rise", "1.0");
        Directory.CreateDirectory(GetVersionRoot(paths, version));
        string scriptsDirectory = Path.Combine(paths.GameDirectory, "Data", "Scripts");
        Directory.CreateDirectory(scriptsDirectory);
        string multiplayerScripts = Path.Combine(scriptsDirectory, "MultiplayerScripts.scb");
        string scriptsIni = Path.Combine(scriptsDirectory, "Scripts.ini");
        File.WriteAllText(multiplayerScripts, "multiplayer");
        File.WriteAllText(scriptsIni, "scripts");
        DeploymentLaunchPreparationService service = CreateService();

        bool prepareSucceeded = service.Prepare(
            new LaunchPreparationRequest(
                paths,
                new[] { version },
                disableBaseGameScriptFiles: true),
            CancellationToken.None);

        prepareSucceeded.Should().BeTrue();
        File.Exists(multiplayerScripts).Should().BeFalse();
        File.Exists(scriptsIni).Should().BeFalse();

        bool cleanupSucceeded = service.Cleanup(paths, CancellationToken.None);

        cleanupSucceeded.Should().BeTrue();
        File.ReadAllText(multiplayerScripts).Should().Be("multiplayer");
        File.ReadAllText(scriptsIni).Should().Be("scripts");
    }

    [Fact]
    public void PrepareReportsDeploymentFailure()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        DeploymentLaunchPreparationService service = CreateService();

        bool succeeded = service.Prepare(
            new LaunchPreparationRequest(
                paths,
                new[] { CreateVersion(ModificationType.Mod, "Missing", "1.0") },
                disableBaseGameScriptFiles: false),
            CancellationToken.None);

        succeeded.Should().BeFalse();
    }

    [Fact]
    public void RecoverRestoresJournaledBackupThroughLaunchPreparationBoundary()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = CreatePaths(directory.Path);
        string deploymentRoot = paths.DeploymentDirectory;
        string backupPath = Path.Combine(deploymentRoot, "Backups", "crash", "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.WriteAllText(backupPath, "original");
        string journalPath = Path.Combine(deploymentRoot, "journal.jsonl");
        DeploymentStateStore.AppendJournal(
            journalPath,
            DeploymentJournalRecord.DeploymentStarted(
                "crash",
                PhysicalDirectoryPath.ResolveExisting(paths.GameDirectory),
                DeploymentStateStore.GetGameRootIdentity(paths.GameDirectory),
                paths.Game));
        byte[] originalBytes = File.ReadAllBytes(backupPath);
        DeploymentStateStore.AppendJournal(
            journalPath,
            DeploymentJournalRecord.FileBackedUp(
                "Data/file.ini",
                "Backups/crash/Data/file.ini",
                new DeploymentFileFingerprint(
                    originalBytes.Length,
                    Convert.ToHexString(SHA256.HashData(originalBytes))),
                "Backups/crash/Data/file.ini.partial"));
        DeploymentLaunchPreparationService service = CreateService();

        bool succeeded = service.Recover(paths, CancellationToken.None);

        succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().Be("original");
        File.Exists(Path.Combine(deploymentRoot, "journal.jsonl")).Should().BeFalse();
    }

    private static DeploymentLaunchPreparationService CreateService()
    {
        var deploymentEngine = new FileSystemDeploymentService(
            new CopyOnlyHardLinkCreator(),
            NullLogger<FileSystemDeploymentService>.Instance);
        return new DeploymentLaunchPreparationService(
            deploymentEngine,
            NullLogger<DeploymentLaunchPreparationService>.Instance);
    }

    private static LauncherContentVersion CreateVersion(
        ModificationType modificationType,
        string name,
        string version,
        string parentContentName = "")
    {
        return new LauncherContentVersion
        {
            ModificationType = modificationType,
            Name = name,
            Version = version,
            ParentContentName = parentContentName,
        };
    }

    private static LauncherPaths CreatePaths(string root)
    {
        string gameDirectory = Path.Combine(root, "Game");
        Directory.CreateDirectory(gameDirectory);

        return TestLauncherPaths.Create(gameDirectory);
    }

    private static void WriteVersionFile(
        LauncherPaths paths,
        LauncherContentVersion version,
        string relativePath,
        string contents)
    {
        string filePath = Path.Combine(GetVersionRoot(paths, version), relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, contents);
    }

    private static string GetVersionRoot(LauncherPaths paths, LauncherContentVersion version)
    {
        return LauncherContentPathResolver.ResolveVersionPath(paths, version.ContentKey)!.FullPath;
    }

    private sealed class CopyOnlyHardLinkCreator : IHardLinkCreator
    {
        public bool TryCreateHardLink(string targetPath, string sourcePath)
        {
            return false;
        }
    }
}
