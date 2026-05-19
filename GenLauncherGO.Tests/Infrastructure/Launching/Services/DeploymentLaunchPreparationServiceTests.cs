using System.IO;
using System.Linq;
using System.Threading;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Mods.Services;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Launching.Services;
using GenLauncherGO.Infrastructure.Launching.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Services;

public sealed class DeploymentLaunchPreparationServiceTests
{
    [Fact]
    public void Prepare_ResolvesSelectedVersionPathsAndUsesSelectionOrderForPrecedence()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        LauncherContentVersion[] versions =
        [
            TestLauncherContent.Version("Rise", "1.0"),
            TestLauncherContent.Version("Balance", "2.0", ModificationType.Patch, "Rise"),
            TestLauncherContent.Version("Maps", "3.0", ModificationType.Addon, "Rise")
        ];
        WriteVersionFile(paths, versions[0], "Data/file.ini", "mod");
        WriteVersionFile(paths, versions[1], "Data/file.ini", "patch");
        WriteVersionFile(paths, versions[2], "Data/file.ini", "addon");
        DeploymentLaunchPreparationService service = CreateService();

        bool succeeded = service.Prepare(
            new LaunchPreparationRequest(
                paths,
                versions,
                false),
            CancellationToken.None);

        succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().Be("addon");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Prepare_RespectsBaseGameScriptSettingAndCleanupRestoresFiles(bool disableBaseGameScripts)
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        LauncherContentVersion version = TestLauncherContent.Version("Rise", "1.0");
        Directory.CreateDirectory(GetVersionRoot(paths, version));
        string scriptsDirectory = Path.Combine(paths.GameDirectory, "Data", "Scripts");
        Directory.CreateDirectory(scriptsDirectory);
        string[] scriptPaths =
        [
            Path.Combine(scriptsDirectory, "MultiplayerScripts.scb"),
            Path.Combine(scriptsDirectory, "SkirmishScripts.scb"),
            Path.Combine(scriptsDirectory, "Scripts.ini")
        ];
        foreach (string scriptPath in scriptPaths)
        {
            File.WriteAllText(scriptPath, Path.GetFileName(scriptPath));
        }

        DeploymentLaunchPreparationService service = CreateService();

        bool prepareSucceeded = service.Prepare(
            new LaunchPreparationRequest(
                paths,
                new[] { version },
                disableBaseGameScripts),
            CancellationToken.None);

        prepareSucceeded.Should().BeTrue();
        foreach (string scriptPath in scriptPaths)
        {
            File.Exists(scriptPath).Should().Be(!disableBaseGameScripts);
        }

        bool cleanupSucceeded = service.Cleanup(paths, CancellationToken.None);

        cleanupSucceeded.Should().BeTrue();
        foreach (string scriptPath in scriptPaths)
        {
            File.ReadAllText(scriptPath).Should().Be(Path.GetFileName(scriptPath));
        }
    }

    [Fact]
    public void Prepare_ReportsDeploymentFailure()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        DeploymentLaunchPreparationService service = CreateService();

        bool succeeded = service.Prepare(
            new LaunchPreparationRequest(
                paths,
                new[] { TestLauncherContent.Version("Missing", "1.0") },
                false),
            CancellationToken.None);

        succeeded.Should().BeFalse();
    }

    [Fact]
    public void Cleanup_WhenBackupIsCorrupt_ReturnsFalse()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        LauncherContentVersion version = TestLauncherContent.Version("Rise", "1.0");
        WriteVersionFile(paths, version, "Data/file.ini", "mod");
        string targetPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, "original");
        DeploymentLaunchPreparationService service = CreateService();
        service.Prepare(
            new LaunchPreparationRequest(paths, new[] { version }, false),
            CancellationToken.None).Should().BeTrue();
        string backupPath = Directory
            .EnumerateFiles(
                Path.Combine(paths.DeploymentDirectory, DeploymentStateStore.BackupsDirectoryName),
                "*",
                SearchOption.AllDirectories)
            .Single();
        File.WriteAllText(backupPath, "corrupt");

        bool succeeded = service.Cleanup(paths, CancellationToken.None);

        succeeded.Should().BeFalse();
        File.ReadAllText(targetPath).Should().Be("mod");
        File.ReadAllText(backupPath).Should().Be("corrupt");
    }

    [Fact]
    public void Recover_WhenManifestAndJournalAreUnreadable_ReturnsFalse()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
        string targetPath = Path.Combine(paths.GameDirectory, "Data", "file.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, "user data");
        File.WriteAllText(Path.Combine(paths.DeploymentDirectory, "active.json"), "{not-json");
        File.WriteAllText(Path.Combine(paths.DeploymentDirectory, "journal.jsonl"), "{also-not-json");
        DeploymentLaunchPreparationService service = CreateService();

        bool succeeded = service.Recover(paths, CancellationToken.None);

        succeeded.Should().BeFalse();
        File.ReadAllText(targetPath).Should().Be("user data");
    }

    [Fact]
    public void Recover_RestoresJournaledBackupThroughLaunchPreparationBoundary()
    {
        using var directory = new TestDirectory();
        LauncherPaths paths = TestLauncherPaths.Create(directory);
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
        DeploymentLaunchPreparationService service = CreateService();

        bool succeeded = service.Recover(paths, CancellationToken.None);

        succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(paths.GameDirectory, "Data", "file.ini")).Should().Be("original");
        File.Exists(Path.Combine(deploymentRoot, "journal.jsonl")).Should().BeFalse();
    }

    private static DeploymentLaunchPreparationService CreateService()
    {
        var deploymentEngine = new FileSystemDeploymentService(
            new FakeHardLinkCreator { CanCreateHardLinks = false },
            NullLogger<FileSystemDeploymentService>.Instance);
        return new DeploymentLaunchPreparationService(
            deploymentEngine,
            NullLogger<DeploymentLaunchPreparationService>.Instance);
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
}
