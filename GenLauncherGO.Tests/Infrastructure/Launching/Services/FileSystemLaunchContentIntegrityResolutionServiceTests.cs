using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Launching.Models;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Integrity.Contracts;
using GenLauncherGO.Infrastructure.Launching.Contracts;
using GenLauncherGO.Infrastructure.Launching.Services;
using GenLauncherGO.Infrastructure.Remote.Contracts;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Services;

public sealed class FileSystemLaunchContentIntegrityResolutionServiceTests
{
    [Fact]
    public async Task VerifyAsync_BuildsTargetsAndVerifiesThemAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateVersion(ContentSourceKind.ManagedSingleFile);
        ContentIntegrityTarget target = CreateTarget("package", Path.Combine(paths.ModsDirectory, "ShockWave", "1.2"));
        LaunchContentIntegrityTargetContext[] contexts =
        [
            new(target, version, false)
        ];
        var report = new ContentIntegrityReport(Array.Empty<ContentIntegrityIssue>());
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        integrityService.VerificationReport = report;
        var targetBuilder = new StubLaunchContentIntegrityTargetBuilder(contexts);
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(
            integrityService,
            targetBuilder);
        var request = new LaunchContentIntegrityTargetRequest(
            paths,
            new[] { version },
            new[] { version },
            "cache");

        LaunchContentIntegrityVerificationResult result = await service.VerifyAsync(
            request,
            CancellationToken.None);

        result.Report.Should().BeSameAs(report);
        result.TargetContexts.Should().Equal(contexts);
        integrityService.VerifiedTargetSets.Should().ContainSingle(targets =>
            targets.Count == 1 &&
            targets[0] == target);
        integrityService.VerifiedPaths.Should().ContainSingle().Which.Should().Be(paths);
    }

    [Theory]
    [InlineData("https://cdn.example.test/card.jpg", "1.2.jpg")]
    [InlineData("https://cdn.example.test/card.webp", "1.2.png")]
    public async Task InitializeUntrackedManagedCachesAsync_CapturesCacheWhenExpectedAssetsMatchAsync(
        string imageSourceLink,
        string expectedImageFileName)
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateVersion(
            ContentSourceKind.ManagedSingleFile,
            imageSourceLink: imageSourceLink);
        ContentIntegrityTarget cacheTarget = CreateTarget(
            "cache",
            paths.GetModificationImagesDirectory("ShockWave"));
        LaunchContentIntegrityTargetContext cacheContext = new(cacheTarget, version, true);
        ContentIntegrityReport report = CreateReport(
            "cache",
            ContentSourceKind.ManagedSingleFile,
            IntegrityIssueKind.Untracked,
            IntegrityIssueAction.Redownload);
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        integrityService.CaptureIfMatchesExpectedFileSetResult = true;
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(integrityService);

        bool initialized = await service.InitializeUntrackedManagedCachesAsync(
            new LaunchContentIntegrityResolutionRequest(paths, report, new[] { cacheContext }),
            CancellationToken.None);

        initialized.Should().BeTrue();
        integrityService.ConditionalSnapshotRequests.Should().ContainSingle(request =>
            request.Target == cacheTarget &&
            request.ExpectedRelativePaths.SetEquals(new[] { expectedImageFileName }));
        integrityService.ConditionalSnapshotPaths.Should().ContainSingle().Which.Should().Be(paths);
    }

    [Fact]
    public async Task InitializeUntrackedManagedCachesAsync_WhenCacheAlsoCarriesAnotherIssue_LeavesItForResolutionAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateVersion(ContentSourceKind.ManagedSingleFile);
        ContentIntegrityTarget cacheTarget = CreateTarget(
            "cache",
            paths.GetModificationImagesDirectory("ShockWave"));
        LaunchContentIntegrityTargetContext cacheContext = new(cacheTarget, version, true);
        ContentIntegrityReport report = CreateReport(
            CreateIssue("cache", ContentSourceKind.ManagedSingleFile, IntegrityIssueKind.Untracked,
                IntegrityIssueAction.Redownload),
            CreateIssue("cache", ContentSourceKind.ManagedSingleFile, IntegrityIssueKind.ModifiedFile,
                IntegrityIssueAction.Repair));
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        integrityService.CaptureIfMatchesExpectedFileSetResult = true;
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(integrityService);

        bool initialized = await service.InitializeUntrackedManagedCachesAsync(
            new LaunchContentIntegrityResolutionRequest(paths, report, new[] { cacheContext }),
            CancellationToken.None);

        initialized.Should().BeFalse();
        integrityService.ConditionalSnapshotRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task InitializeUntrackedManagedCachesAsync_WhenCacheContentsDoNotMatch_ReportsNoInitializationAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateVersion(ContentSourceKind.ManagedSingleFile);
        ContentIntegrityTarget cacheTarget = CreateTarget(
            "cache",
            paths.GetModificationImagesDirectory("ShockWave"));
        LaunchContentIntegrityTargetContext cacheContext = new(cacheTarget, version, true);
        ContentIntegrityReport report = CreateReport(
            "cache",
            ContentSourceKind.ManagedSingleFile,
            IntegrityIssueKind.Untracked,
            IntegrityIssueAction.Redownload);
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        integrityService.CaptureIfMatchesExpectedFileSetResult = false;
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(integrityService);

        bool initialized = await service.InitializeUntrackedManagedCachesAsync(
            new LaunchContentIntegrityResolutionRequest(paths, report, new[] { cacheContext }),
            CancellationToken.None);

        initialized.Should().BeFalse();
        integrityService.ConditionalSnapshotRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task InitializeUntrackedManagedCachesAsync_WithUntrackedPackage_LeavesItForResolutionAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateVersion(ContentSourceKind.ManagedSingleFile);
        ContentIntegrityTarget packageTarget = CreateTarget(
            "package",
            Path.Combine(paths.ModsDirectory, "ShockWave", "1.2"));
        LaunchContentIntegrityTargetContext packageContext = new(packageTarget, version, false);
        ContentIntegrityReport report = CreateReport(
            "package",
            ContentSourceKind.ManagedSingleFile,
            IntegrityIssueKind.Untracked,
            IntegrityIssueAction.Redownload);
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        integrityService.CaptureIfMatchesExpectedFileSetResult = true;
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(integrityService);

        bool initialized = await service.InitializeUntrackedManagedCachesAsync(
            new LaunchContentIntegrityResolutionRequest(paths, report, new[] { packageContext }),
            CancellationToken.None);

        initialized.Should().BeFalse();
        integrityService.ConditionalSnapshotRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_RefreshesManagedCacheAndPreservesIgnoredFilesAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateVersion(ContentSourceKind.ManagedSingleFile);
        string cacheRoot = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(cacheRoot);
        string staleFilePath = Path.Combine(cacheRoot, "stale.txt");
        string ignoredFilePath = Path.Combine(cacheRoot, "ignored.txt");
        // A nested folder, so the sweep has to empty it from the deepest level upwards. Removing the child first is
        // what lets its parent be empty in the same pass; the other order leaves the outer folder standing forever.
        string staleDirectory = Path.Combine(cacheRoot, "Stale");
        string staleNestedDirectory = Path.Combine(staleDirectory, "Nested");
        Directory.CreateDirectory(staleNestedDirectory);
        await File.WriteAllTextAsync(staleFilePath, "stale");
        await File.WriteAllTextAsync(ignoredFilePath, "ignored");
        await File.WriteAllTextAsync(Path.Combine(staleNestedDirectory, "nested.txt"), "nested");

        ContentIntegrityTarget cacheTarget = CreateTarget(
            "cache",
            cacheRoot,
            ContentSourceKind.ManagedSingleFile,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ignored.txt" });
        LaunchContentIntegrityTargetContext cacheContext = new(cacheTarget, version, true);
        ContentIntegrityReport report = CreateReport(
            "cache",
            ContentSourceKind.ManagedSingleFile,
            IntegrityIssueKind.ModifiedFile,
            IntegrityIssueAction.Repair);
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        var assetDownloader = new RecordingRemoteAssetDownloader();
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(
            integrityService,
            assetDownloader: assetDownloader);
        RecordingProgress<LaunchContentIntegrityResolutionProgress> progress = new();

        await service.ResolveAsync(
            new LaunchContentIntegrityResolutionRequest(paths, report, new[] { cacheContext }),
            progress,
            CancellationToken.None);

        File.Exists(staleFilePath).Should().BeFalse();
        File.Exists(ignoredFilePath).Should().BeTrue();
        Directory.Exists(staleNestedDirectory).Should().BeFalse();
        Directory.Exists(staleDirectory).Should().BeFalse();
        assetDownloader.Calls.Should().ContainSingle(call =>
            call.SourceUri == new Uri("https://cdn.example.test/card.jpg") &&
            call.DestinationFilePath == Path.Combine(cacheRoot, "1.2.jpg"));
        progress.Reports.Should().ContainSingle(report =>
            report.TargetId == "cache" &&
            report.Completed &&
            report.PackageProgress == null);
        integrityService.CapturedTargets.Should().ContainSingle().Which.Should().Be(cacheTarget);
    }

    [Fact]
    public async Task ResolveAsync_RepairsManagedSingleFilePackageAndReportsProgressAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateVersion(ContentSourceKind.ManagedSingleFile);
        ContentIntegrityTarget packageTarget = CreateTarget(
            "package",
            Path.Combine(paths.ModsDirectory, "ShockWave", "1.2"));
        LaunchContentIntegrityTargetContext packageContext = new(packageTarget, version, false);
        ContentIntegrityReport report = CreateReport(
            "package",
            ContentSourceKind.ManagedSingleFile,
            IntegrityIssueKind.MissingFile,
            IntegrityIssueAction.Repair);
        PackageUpdateProgress packageProgress = new(100, 40, 40, "package.zip");
        var singleFilePackageUpdater = new RecordingSingleFilePackageUpdater
        {
            ProgressToReport = packageProgress
        };
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(
            integrityService,
            singleFilePackageUpdater: singleFilePackageUpdater);
        RecordingProgress<LaunchContentIntegrityResolutionProgress> progress = new();

        await service.ResolveAsync(
            new LaunchContentIntegrityResolutionRequest(paths, report, new[] { packageContext }),
            progress,
            CancellationToken.None);

        (Uri SourceUri, PackageUpdatePathSet Paths) updateRequest =
            singleFilePackageUpdater.Requests.Should().ContainSingle().Which;
        updateRequest.SourceUri.Should().Be(new Uri("https://www.dropbox.com/s/package/file.zip?dl=1"));
        updateRequest.Paths.TemporaryPath.FullPath.Should()
            .Be(Path.Combine(paths.TempDirectory, "Packages", "ShockWave", "1.2"));
        updateRequest.Paths.InstalledPath.FullPath.Should().Be(packageTarget.RootDirectory);
        progress.Reports.Should().ContainSingle(report =>
            report.TargetId == "package" &&
            report.PackageProgress == packageProgress &&
            !report.Completed);
        integrityService.CapturedTargets.Should().ContainSingle().Which.Should().Be(packageTarget);
    }

    [Fact]
    public async Task ResolveAsync_AppliesCleanupToEveryTargetBeforeRepairingAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateVersion(ContentSourceKind.ManagedSingleFile);
        ContentIntegrityTarget packageTarget = CreateTarget(
            "package",
            Path.Combine(paths.ModsDirectory, "ShockWave", "1.2"));
        ContentIntegrityTarget cacheTarget = CreateTarget(
            "cache",
            paths.GetModificationImagesDirectory("ShockWave"));
        LaunchContentIntegrityTargetContext[] contexts =
        [
            new(packageTarget, version, false),
            new(cacheTarget, version, true)
        ];
        ContentIntegrityReport report = CreateReport(
            "package",
            ContentSourceKind.ManagedSingleFile,
            IntegrityIssueKind.MissingFile,
            IntegrityIssueAction.Repair);
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        var singleFilePackageUpdater = new RecordingSingleFilePackageUpdater
        {
            Update = (_, _, _) =>
            {
                integrityService.Calls.Add("repair");
                return Task.CompletedTask;
            }
        };
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(
            integrityService,
            singleFilePackageUpdater: singleFilePackageUpdater);

        await service.ResolveAsync(
            new LaunchContentIntegrityResolutionRequest(paths, report, contexts),
            null,
            CancellationToken.None);

        (ContentIntegrityReport Report, IReadOnlyList<ContentIntegrityTarget> Targets) cleanupRequest =
            integrityService.CleanupRequests.Should().ContainSingle().Which;
        cleanupRequest.Report.Should().BeSameAs(report);
        cleanupRequest.Targets.Should().Equal(packageTarget, cacheTarget);
        integrityService.Calls.Should().Equal("cleanup", "repair", "capture");
    }

    [Fact]
    public async Task ResolveAsync_TrustAsManualIssue_MarksVersionManualAndSnapshotsItAsManualAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateVersion(ContentSourceKind.UnknownLegacy);
        ContentIntegrityTarget packageTarget = CreateTarget(
            "package",
            Path.Combine(paths.ModsDirectory, "ShockWave", "1.2"),
            ContentSourceKind.UnknownLegacy);
        LaunchContentIntegrityTargetContext packageContext = new(packageTarget, version, false);
        ContentIntegrityReport report = CreateReport(
            "package",
            ContentSourceKind.UnknownLegacy,
            IntegrityIssueKind.Untracked,
            IntegrityIssueAction.TrustAsManual);
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        var catalog = new FakeLauncherContentCatalog();
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(
            integrityService,
            catalog: catalog);

        await service.ResolveAsync(
            new LaunchContentIntegrityResolutionRequest(paths, report, new[] { packageContext }),
            null,
            CancellationToken.None);

        version.Installation.ContentSourceKind.Should().Be(ContentSourceKind.Manual);
        catalog.SaveCount.Should().Be(1);
        ContentIntegrityTarget capturedTarget = integrityService.CapturedTargets.Should().ContainSingle().Which;
        capturedTarget.Id.Should().Be(packageTarget.Id);
        capturedTarget.SourceKind.Should().Be(ContentSourceKind.Manual);
    }

    [Fact]
    public async Task ResolveAsync_AbsorbIssue_SnapshotsTheTargetWithoutRepairingItAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateVersion(ContentSourceKind.Manual);
        ContentIntegrityTarget packageTarget = CreateTarget(
            "package",
            Path.Combine(paths.ModsDirectory, "ShockWave", "1.2"),
            ContentSourceKind.Manual);
        LaunchContentIntegrityTargetContext packageContext = new(packageTarget, version, false);
        ContentIntegrityReport report = CreateReport(
            "package",
            ContentSourceKind.Manual,
            IntegrityIssueKind.Untracked,
            IntegrityIssueAction.Absorb);
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        var singleFilePackageUpdater = new RecordingSingleFilePackageUpdater();
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(
            integrityService,
            singleFilePackageUpdater: singleFilePackageUpdater);

        await service.ResolveAsync(
            new LaunchContentIntegrityResolutionRequest(paths, report, new[] { packageContext }),
            null,
            CancellationToken.None);

        integrityService.CapturedTargets.Should().ContainSingle().Which.Should().BeSameAs(packageTarget);
        version.Installation.ContentSourceKind.Should().Be(ContentSourceKind.Manual);
        singleFilePackageUpdater.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_RepairsManagedS3PackageFileInPlaceUsingManifestAndTargetRootAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = TestLauncherContent.S3Version(version: "1.2");
        ContentIntegrityTarget packageTarget = CreateTarget(
            "package",
            Path.Combine(paths.ModsDirectory, "ShockWave", "1.2"),
            ContentSourceKind.ManagedS3);
        LaunchContentIntegrityTargetContext packageContext = new(packageTarget, version, false);
        ContentIntegrityReport report = CreateReport(
            "package",
            ContentSourceKind.ManagedS3,
            IntegrityIssueKind.ModifiedFile,
            IntegrityIssueAction.Repair,
            "Data/file.gib");
        RemoteFileManifestEntry[] files =
        [
            new("Data/file.big", StubFileHashService.MatchingHash, 10),
            new("Data/readme.txt", StubFileHashService.MatchingHash, 5)
        ];
        var manifestReader = new RecordingS3ObjectManifestReader();
        manifestReader.Enqueue(files);
        PackageUpdateProgress packageProgress = new(10, 10, 100, "Data/file.big");
        var s3PackageUpdater = new RecordingS3PackageUpdater { ProgressToReport = packageProgress };
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(
            integrityService,
            manifestReader: manifestReader,
            s3PackageUpdater: s3PackageUpdater);
        RecordingProgress<LaunchContentIntegrityResolutionProgress> progress = new();

        await service.ResolveAsync(
            new LaunchContentIntegrityResolutionRequest(paths, report, new[] { packageContext }),
            progress,
            CancellationToken.None);

        manifestReader.Requests.Should().ContainSingle().Which.Should().Match<S3ObjectManifestRequest>(request =>
            request.Endpoint == TestLauncherContent.S3Host &&
            request.BucketName == TestLauncherContent.S3Bucket &&
            request.Prefix == "ShockWave/1.2");
        S3PackageFileRepairRequest repairRequest =
            s3PackageUpdater.RepairRequests.Should().ContainSingle().Which;
        repairRequest.Files.Should().ContainSingle().Which.FileName.Should().Be("Data/file.big");
        repairRequest.Source.Should().BeSameAs(manifestReader.Requests.Single());
        repairRequest.InstalledPath.FullPath.Should().Be(packageTarget.RootDirectory);
        repairRequest.InstalledPath.OwnerRoot.Should().Be(paths.ModsDirectory);
        repairRequest.HashCheckedExtensions.Should().BeEquivalentTo(".big", ".txt", ".gib");
        progress.Reports.Should().ContainSingle(report =>
            report.TargetId == "package" &&
            report.PackageProgress == packageProgress);
        integrityService.CapturedTargets.Should().ContainSingle().Which.Should().Be(packageTarget);
        s3PackageUpdater.UpdateRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_RepairsUntrackedManagedS3PackageUsingFullReplacementAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = TestLauncherContent.S3Version(version: "1.2");
        ContentIntegrityTarget packageTarget = CreateTarget(
            "package",
            Path.Combine(paths.ModsDirectory, "ShockWave", "1.2"),
            ContentSourceKind.ManagedS3);
        LaunchContentIntegrityTargetContext packageContext = new(packageTarget, version, false);
        ContentIntegrityReport report = CreateReport(
            "package",
            ContentSourceKind.ManagedS3,
            IntegrityIssueKind.Untracked,
            IntegrityIssueAction.Repair,
            ".");
        RemoteFileManifestEntry[] files =
        [
            new("Data/file.big", StubFileHashService.MatchingHash, 10),
            new("Data/readme.txt", StubFileHashService.MatchingHash, 5)
        ];
        var manifestReader = new RecordingS3ObjectManifestReader();
        manifestReader.Enqueue(files);
        var s3PackageUpdater = new RecordingS3PackageUpdater();
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(
            integrityService,
            manifestReader: manifestReader,
            s3PackageUpdater: s3PackageUpdater);

        await service.ResolveAsync(
            new LaunchContentIntegrityResolutionRequest(paths, report, new[] { packageContext }),
            null,
            CancellationToken.None);

        S3PackageUpdateRequest updateRequest =
            s3PackageUpdater.UpdateRequests.Should().ContainSingle().Which;
        updateRequest.Files.Should().Equal(files);
        updateRequest.Source.Should().BeSameAs(manifestReader.Requests.Single());
        updateRequest.PathSet.InstalledPath.FullPath.Should().Be(packageTarget.RootDirectory);
        updateRequest.PathSet.BackupPath.FullPath.Should().Be(
            Path.Combine(paths.StateDirectory, "PackageBackups", "ShockWave", "1.2"));
        updateRequest.PathSet.LatestInstalledPath!.FullPath.Should().Be(packageTarget.RootDirectory);
        s3PackageUpdater.RepairRequests.Should().BeEmpty();
        integrityService.CapturedTargets.Should().ContainSingle().Which.Should().Be(packageTarget);
    }

    /// <summary>
    ///     A package holding an extra file beside a modified one cannot be mended file by file: replacing the
    ///     modified file leaves the untracked one exactly where it was. Any issue that is not a file-level repair
    ///     therefore has to send the whole package down the replacement path, even when every reported path does
    ///     map to a manifest entry and a partial repair would otherwise look possible.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_UntrackedFileBesideAModifiedOne_ReplacesRatherThanRepairsAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = TestLauncherContent.S3Version(version: "1.2");
        ContentIntegrityTarget packageTarget = CreateTarget(
            "package",
            Path.Combine(paths.ModsDirectory, "ShockWave", "1.2"),
            ContentSourceKind.ManagedS3);
        LaunchContentIntegrityTargetContext packageContext = new(packageTarget, version, false);
        ContentIntegrityReport report = CreateReport(
            CreateIssue(
                "package",
                ContentSourceKind.ManagedS3,
                IntegrityIssueKind.ModifiedFile,
                IntegrityIssueAction.Repair,
                "Data/file.gib"),
            CreateIssue(
                "package",
                ContentSourceKind.ManagedS3,
                IntegrityIssueKind.Untracked,
                IntegrityIssueAction.Repair,
                "Data/readme.txt"));
        RemoteFileManifestEntry[] files =
        [
            new("Data/file.big", StubFileHashService.MatchingHash, 10),
            new("Data/readme.txt", StubFileHashService.MatchingHash, 5)
        ];
        var manifestReader = new RecordingS3ObjectManifestReader();
        manifestReader.Enqueue(files);
        var s3PackageUpdater = new RecordingS3PackageUpdater();
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(
            integrityService,
            manifestReader: manifestReader,
            s3PackageUpdater: s3PackageUpdater);

        await service.ResolveAsync(
            new LaunchContentIntegrityResolutionRequest(paths, report, new[] { packageContext }),
            null,
            CancellationToken.None);

        s3PackageUpdater.RepairRequests.Should().BeEmpty(
            "an untracked file survives an in-place repair, so the package has to be replaced whole");
        s3PackageUpdater.UpdateRequests.Should().ContainSingle().Which.Files.Should().Equal(files);
    }

    [Fact]
    public async Task RegisterManualImportAsync_MarksVersionManualAndCapturesPackageAndCacheSnapshotsAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateVersion(ContentSourceKind.UnknownLegacy);
        ContentIntegrityTarget packageTarget = CreateTarget(
            "package",
            Path.Combine(paths.ModsDirectory, "ShockWave", "1.2"),
            ContentSourceKind.Manual);
        ContentIntegrityTarget cacheTarget = CreateTarget(
            "cache",
            paths.GetModificationImagesDirectory("ShockWave"),
            ContentSourceKind.Manual);
        LaunchContentIntegrityTargetContext[] contexts =
        [
            new(packageTarget, version, false),
            new(cacheTarget, version, true)
        ];
        var targetBuilder = new StubLaunchContentIntegrityTargetBuilder(contexts);
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        var catalog = new FakeLauncherContentCatalog();
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(
            integrityService,
            targetBuilder,
            catalog: catalog);

        await service.RegisterManualImportAsync(
            new LaunchContentIntegrityVersionRequest(paths, version, new[] { version }, "cache"),
            CancellationToken.None);

        version.Installation.ContentSourceKind.Should().Be(ContentSourceKind.Manual);
        catalog.SaveCount.Should().Be(1);
        targetBuilder.Requests.Should().Contain(request =>
            request.Paths == paths &&
            request.ActiveVersions.Count == 1 &&
            request.ActiveVersions[0] == version &&
            request.CacheDisplayNameSuffix == "cache");
        integrityService.CapturedTargets.Should().BeEquivalentTo(new[] { packageTarget, cacheTarget });
        integrityService.CapturedPaths.Should().OnlyContain(capturedPaths => capturedPaths == paths);
    }

    [Fact]
    public async Task CaptureManagedInstallSnapshotAsync_RefreshesMismatchedThemedImageCacheAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateVersion(
            ContentSourceKind.ManagedSingleFile,
            theme: new LauncherContentTheme
            {
                GenLauncherBackgroundImageLink = "https://cdn.example.test/background.png"
            });
        LauncherContentVersion inactiveVersion = CreateVersion(
            ContentSourceKind.ManagedSingleFile,
            "1.1");
        string cacheRoot = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(cacheRoot);
        string staleFilePath = Path.Combine(cacheRoot, "stale.txt");
        string inactiveImagePath = Path.Combine(cacheRoot, "1.1.jpg");
        string themeCachePath = Path.Combine(
            cacheRoot,
            LauncherContentTheme.ResolveCacheBaseName("1.2") + ".yaml");
        string backgroundPath = Path.Combine(cacheRoot, LauncherContentTheme.ResolveBackgroundImageBaseName("1.2") + ".png");
        await File.WriteAllTextAsync(staleFilePath, "stale");
        await File.WriteAllTextAsync(inactiveImagePath, "inactive");
        await File.WriteAllTextAsync(themeCachePath, "theme");
        await File.WriteAllTextAsync(backgroundPath, "old background");
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        var assetDownloader = new RecordingRemoteAssetDownloader
        {
            Handler = (_, destinationPath, cancellationToken) =>
                File.WriteAllTextAsync(destinationPath, "downloaded", cancellationToken)
        };
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(
            integrityService,
            new FileSystemLaunchContentIntegrityTargetBuilder(),
            assetDownloader: assetDownloader);

        await service.CaptureManagedInstallSnapshotAsync(
            new LaunchContentIntegrityVersionRequest(
                paths,
                version,
                new[] { version, inactiveVersion },
                "cache"),
            CancellationToken.None);

        ContentIntegrityTarget packageTarget = integrityService.CapturedTargets[0];
        packageTarget.Id.Should().Be("package:mod::shockwave:1.2");
        packageTarget.RootDirectory.Should().Be(Path.Combine(paths.ModsDirectory, "ShockWave", "1.2"));
        packageTarget.SourceKind.Should().Be(ContentSourceKind.ManagedSingleFile);

        (ContentIntegrityTarget Target, IReadOnlySet<string> ExpectedRelativePaths) conditionalSnapshot =
            integrityService.ConditionalSnapshotRequests.Should().ContainSingle().Which;
        conditionalSnapshot.Target.Id.Should().Be("cache:mod::shockwave:1.2");
        conditionalSnapshot.Target.RootDirectory.Should().Be(cacheRoot);
        conditionalSnapshot.Target.IgnoredRelativePaths.Should().BeEquivalentTo(
            "1.1.jpg",
            LauncherContentTheme.ResolveCacheBaseName("1.2") + ".yaml");
        conditionalSnapshot.ExpectedRelativePaths.Should().BeEquivalentTo(
            "1.2.jpg",
            LauncherContentTheme.ResolveBackgroundImageBaseName("1.2") + ".png");

        File.Exists(staleFilePath).Should().BeFalse();
        File.Exists(inactiveImagePath).Should().BeTrue();
        File.Exists(themeCachePath).Should().BeTrue();
        (await File.ReadAllTextAsync(backgroundPath)).Should().Be("downloaded");
        assetDownloader.Calls.Should().BeEquivalentTo(new[]
        {
            (
                new Uri("https://cdn.example.test/card.jpg"),
                Path.Combine(cacheRoot, "1.2.jpg")),
            (
                new Uri("https://cdn.example.test/background.png"),
                Path.Combine(cacheRoot, LauncherContentTheme.ResolveBackgroundImageBaseName("1.2") + ".png"))
        });
        integrityService.CapturedTargets.Should().HaveCount(2);
        integrityService.CapturedTargets[1].Should().Be(conditionalSnapshot.Target);
    }

    [Fact]
    public async Task CaptureManualImageSnapshotAsync_CapturesOnlyResolvedManualImageCacheAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateVersion(
            ContentSourceKind.Manual,
            simpleDownloadLink: string.Empty);
        LauncherContentVersion inactiveVersion = CreateVersion(
            ContentSourceKind.Manual,
            "1.1",
            string.Empty);
        string cacheRoot = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(cacheRoot);
        await File.WriteAllTextAsync(Path.Combine(cacheRoot, LauncherContentTheme.ResolveBackgroundImageBaseName("1.1") + ".png"), "inactive");
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        var catalog = new FakeLauncherContentCatalog();
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(
            integrityService,
            new FileSystemLaunchContentIntegrityTargetBuilder(),
            catalog: catalog);

        await service.CaptureManualImageSnapshotAsync(
            new LaunchContentIntegrityVersionRequest(
                paths,
                version,
                new[] { version, inactiveVersion },
                "image cache"),
            CancellationToken.None);

        ContentIntegrityTarget cacheTarget = integrityService.CapturedTargets.Should().ContainSingle().Which;
        cacheTarget.Id.Should().Be("cache:mod::shockwave:1.2");
        cacheTarget.DisplayName.Should().Be("ShockWave 1.2 image cache");
        cacheTarget.RootDirectory.Should().Be(cacheRoot);
        cacheTarget.SourceKind.Should().Be(ContentSourceKind.Manual);
        cacheTarget.IgnoredRelativePaths.Should().ContainSingle().Which.Should().Be(LauncherContentTheme.ResolveBackgroundImageBaseName("1.1") + ".png");
        integrityService.ConditionalSnapshotRequests.Should().BeEmpty();
        catalog.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task InitializeUntrackedManagedCachesAsync_WithUntrackedManualCache_LeavesItForResolutionAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateVersion(ContentSourceKind.Manual);
        ContentIntegrityTarget cacheTarget = CreateTarget(
            "cache",
            paths.GetModificationImagesDirectory("ShockWave"),
            ContentSourceKind.Manual);
        LaunchContentIntegrityTargetContext cacheContext = new(cacheTarget, version, true);
        ContentIntegrityReport report = CreateReport(
            "cache",
            ContentSourceKind.Manual,
            IntegrityIssueKind.Untracked,
            IntegrityIssueAction.TrustAsManual);
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        integrityService.CaptureIfMatchesExpectedFileSetResult = true;
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(integrityService);

        bool initialized = await service.InitializeUntrackedManagedCachesAsync(
            new LaunchContentIntegrityResolutionRequest(paths, report, new[] { cacheContext }),
            CancellationToken.None);

        initialized.Should().BeFalse();
        integrityService.ConditionalSnapshotRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_TrustAsManualIssuesForSeveralTargets_MarksEveryReportedVersionManualAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion firstVersion = CreateVersion(ContentSourceKind.UnknownLegacy);
        LauncherContentVersion secondVersion = CreateVersion(ContentSourceKind.UnknownLegacy, "1.3");
        LaunchContentIntegrityTargetContext firstContext = new(
            CreateTarget(
                "package-first",
                Path.Combine(paths.ModsDirectory, "ShockWave", "1.2"),
                ContentSourceKind.UnknownLegacy),
            firstVersion,
            false);
        LaunchContentIntegrityTargetContext secondContext = new(
            CreateTarget(
                "package-second",
                Path.Combine(paths.ModsDirectory, "ShockWave", "1.3"),
                ContentSourceKind.UnknownLegacy),
            secondVersion,
            false);
        ContentIntegrityReport report = CreateReport(
            CreateIssue(
                "package-first",
                ContentSourceKind.UnknownLegacy,
                IntegrityIssueKind.Untracked,
                IntegrityIssueAction.TrustAsManual),
            CreateIssue(
                "package-second",
                ContentSourceKind.UnknownLegacy,
                IntegrityIssueKind.Untracked,
                IntegrityIssueAction.TrustAsManual));
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(integrityService);

        await service.ResolveAsync(
            new LaunchContentIntegrityResolutionRequest(paths, report, new[] { firstContext, secondContext }),
            null,
            CancellationToken.None);

        firstVersion.Installation.ContentSourceKind.Should().Be(ContentSourceKind.Manual);
        secondVersion.Installation.ContentSourceKind.Should().Be(ContentSourceKind.Manual);
        integrityService.CapturedTargets.Select(target => target.Id).Should()
            .Equal("package-first", "package-second");
    }

    [Fact]
    public async Task ResolveAsync_AbsorbIssuesForSeveralTargets_SnapshotsEveryReportedTargetAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        ContentIntegrityTarget firstTarget = CreateTarget(
            "package-first",
            Path.Combine(paths.ModsDirectory, "ShockWave", "1.2"),
            ContentSourceKind.Manual);
        ContentIntegrityTarget secondTarget = CreateTarget(
            "package-second",
            Path.Combine(paths.ModsDirectory, "ShockWave", "1.3"),
            ContentSourceKind.Manual);
        LaunchContentIntegrityTargetContext firstContext =
            new(firstTarget, CreateVersion(ContentSourceKind.Manual), false);
        LaunchContentIntegrityTargetContext secondContext =
            new(secondTarget, CreateVersion(ContentSourceKind.Manual, "1.3"), false);
        ContentIntegrityReport report = CreateReport(
            CreateIssue(
                "package-first",
                ContentSourceKind.Manual,
                IntegrityIssueKind.Untracked,
                IntegrityIssueAction.Absorb),
            CreateIssue(
                "package-second",
                ContentSourceKind.Manual,
                IntegrityIssueKind.Untracked,
                IntegrityIssueAction.Absorb));
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(integrityService);

        await service.ResolveAsync(
            new LaunchContentIntegrityResolutionRequest(paths, report, new[] { firstContext, secondContext }),
            null,
            CancellationToken.None);

        integrityService.CapturedTargets.Should().Equal(firstTarget, secondTarget);
    }

    [Fact]
    public async Task ResolveAsync_RepairIssuesForSeveralPackages_RepairsEveryReportedPackageAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LaunchContentIntegrityTargetContext firstContext = new(
            CreateTarget(
                "package-first",
                Path.Combine(paths.ModsDirectory, "ShockWave", "1.2"),
                ContentSourceKind.ManagedSingleFile),
            CreateVersion(ContentSourceKind.ManagedSingleFile),
            false);
        LaunchContentIntegrityTargetContext secondContext = new(
            CreateTarget(
                "package-second",
                Path.Combine(paths.ModsDirectory, "ShockWave", "1.3"),
                ContentSourceKind.ManagedSingleFile),
            CreateVersion(ContentSourceKind.ManagedSingleFile, "1.3"),
            false);
        ContentIntegrityReport report = CreateReport(
            CreateIssue(
                "package-first",
                ContentSourceKind.ManagedSingleFile,
                IntegrityIssueKind.ModifiedFile,
                IntegrityIssueAction.Repair),
            CreateIssue(
                "package-second",
                ContentSourceKind.ManagedSingleFile,
                IntegrityIssueKind.ModifiedFile,
                IntegrityIssueAction.Repair));
        var singleFilePackageUpdater = new RecordingSingleFilePackageUpdater();
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(
            singleFilePackageUpdater: singleFilePackageUpdater);

        await service.ResolveAsync(
            new LaunchContentIntegrityResolutionRequest(paths, report, new[] { firstContext, secondContext }),
            null,
            CancellationToken.None);

        singleFilePackageUpdater.Requests.Should().HaveCount(2);
    }

    private static FileSystemLaunchContentIntegrityResolutionService CreateService(
        IContentIntegrityService? integrityService = null,
        ILaunchContentIntegrityTargetBuilder? targetBuilder = null,
        IS3ObjectManifestReader? manifestReader = null,
        IS3PackageUpdater? s3PackageUpdater = null,
        ISingleFilePackageUpdater? singleFilePackageUpdater = null,
        IRemoteAssetDownloader? assetDownloader = null,
        ILauncherContentCatalog? catalog = null)
    {
        return new FileSystemLaunchContentIntegrityResolutionService(
            integrityService ?? CreateIntegrityService(),
            targetBuilder ?? new StubLaunchContentIntegrityTargetBuilder(),
            manifestReader ?? new RecordingS3ObjectManifestReader(),
            s3PackageUpdater ?? new RecordingS3PackageUpdater(),
            singleFilePackageUpdater ?? new RecordingSingleFilePackageUpdater(),
            assetDownloader ?? new RecordingRemoteAssetDownloader(),
            catalog ?? new FakeLauncherContentCatalog(),
            NullLogger<FileSystemLaunchContentIntegrityResolutionService>.Instance);
    }

    private static RecordingContentIntegrityService CreateIntegrityService()
    {
        return new RecordingContentIntegrityService();
    }

    private static ContentIntegrityReport CreateReport(
        string targetId,
        ContentSourceKind sourceKind,
        IntegrityIssueKind issueKind,
        IntegrityIssueAction action,
        string relativePath = "Data/file.big")
    {
        return CreateReport(CreateIssue(targetId, sourceKind, issueKind, action, relativePath));
    }

    private static ContentIntegrityReport CreateReport(params ContentIntegrityIssue[] issues)
    {
        return new ContentIntegrityReport(issues);
    }

    private static ContentIntegrityIssue CreateIssue(
        string targetId,
        ContentSourceKind sourceKind,
        IntegrityIssueKind issueKind,
        IntegrityIssueAction action,
        string relativePath = "Data/file.big")
    {
        return new ContentIntegrityIssue(
            targetId,
            "ShockWave 1.2",
            sourceKind,
            issueKind,
            action,
            relativePath);
    }

    private static ContentIntegrityTarget CreateTarget(
        string id,
        string rootDirectory,
        ContentSourceKind sourceKind = ContentSourceKind.ManagedSingleFile,
        IReadOnlySet<string>? ignoredRelativePaths = null)
    {
        return new ContentIntegrityTarget(
            id,
            "ShockWave 1.2",
            rootDirectory,
            sourceKind,
            ignoredRelativePaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Builds a version carrying the remote image link the shared content builder does not expose, because
    ///     <see cref="LauncherContentVersion.UIImageSourceLink" /> is initialization-only.
    /// </summary>
    private static LauncherContentVersion CreateVersion(
        ContentSourceKind sourceKind,
        string version = "1.2",
        string simpleDownloadLink = "https://www.dropbox.com/s/package/file.zip?dl=0",
        string imageSourceLink = "https://cdn.example.test/card.jpg",
        LauncherContentTheme? theme = null)
    {
        return new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { ContentSourceKind = sourceKind },
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = version,
            SimpleDownloadLink = simpleDownloadLink,
            UIImageSourceLink = imageSourceLink,
            Theme = theme
        };
    }


    private sealed class RecordingContentIntegrityService : IContentIntegrityService
    {
        public ContentIntegrityReport VerificationReport { get; set; } =
            new(Array.Empty<ContentIntegrityIssue>());

        public bool CaptureIfMatchesExpectedFileSetResult { get; set; }

        public List<IReadOnlyList<ContentIntegrityTarget>> VerifiedTargetSets { get; } = [];

        public List<LauncherPaths> VerifiedPaths { get; } = [];

        public List<(
            ContentIntegrityTarget Target,
            IReadOnlySet<string> ExpectedRelativePaths)> ConditionalSnapshotRequests
        { get; } = [];

        public List<LauncherPaths> ConditionalSnapshotPaths { get; } = [];

        public List<ContentIntegrityTarget> CapturedTargets { get; } = [];

        public List<LauncherPaths> CapturedPaths { get; } = [];

        public List<(
            ContentIntegrityReport Report,
            IReadOnlyList<ContentIntegrityTarget> Targets)> CleanupRequests
        { get; } = [];

        /// <summary>
        ///     Names every observed call in order, so a test can pin that cleanup runs before a repair rewrites files.
        /// </summary>
        public List<string> Calls { get; } = [];

        public Task<ContentIntegrityReport> VerifyAsync(
            LauncherPaths paths,
            IReadOnlyList<ContentIntegrityTarget> targets,
            CancellationToken cancellationToken)
        {
            Calls.Add("verify");
            VerifiedPaths.Add(paths);
            VerifiedTargetSets.Add(targets);
            return Task.FromResult(VerificationReport);
        }

        public Task<bool> CaptureSnapshotIfMatchesExpectedFileSetAsync(
            LauncherPaths paths,
            ContentIntegrityTarget target,
            IReadOnlySet<string> expectedRelativePaths,
            CancellationToken cancellationToken)
        {
            Calls.Add("conditional-capture");
            ConditionalSnapshotPaths.Add(paths);
            ConditionalSnapshotRequests.Add((target, expectedRelativePaths));
            return Task.FromResult(CaptureIfMatchesExpectedFileSetResult);
        }

        public Task CaptureSnapshotAsync(
            LauncherPaths paths,
            ContentIntegrityTarget target,
            CancellationToken cancellationToken)
        {
            Calls.Add("capture");
            CapturedPaths.Add(paths);
            CapturedTargets.Add(target);
            return Task.CompletedTask;
        }

        public Task ApplyCleanupAsync(
            ContentIntegrityReport report,
            IReadOnlyList<ContentIntegrityTarget> targets,
            CancellationToken cancellationToken)
        {
            Calls.Add("cleanup");
            CleanupRequests.Add((report, targets));
            return Task.CompletedTask;
        }
    }

    private sealed class StubLaunchContentIntegrityTargetBuilder : ILaunchContentIntegrityTargetBuilder
    {
        private readonly IReadOnlyList<LaunchContentIntegrityTargetContext> _contexts;

        public StubLaunchContentIntegrityTargetBuilder()
            : this(Array.Empty<LaunchContentIntegrityTargetContext>())
        {
        }

        public StubLaunchContentIntegrityTargetBuilder(
            IReadOnlyList<LaunchContentIntegrityTargetContext> contexts)
        {
            _contexts = contexts;
        }

        public List<LaunchContentIntegrityTargetRequest> Requests { get; } = [];

        public IReadOnlyList<LaunchContentIntegrityTargetContext> BuildTargets(
            LaunchContentIntegrityTargetRequest request)
        {
            Requests.Add(request);
            return _contexts;
        }
    }
}
