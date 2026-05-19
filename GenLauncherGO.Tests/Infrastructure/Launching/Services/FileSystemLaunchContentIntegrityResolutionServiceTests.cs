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
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Launching.Services;

public sealed class FileSystemLaunchContentIntegrityResolutionServiceTests
{
    [Fact]
    public async Task VerifyAsync_BuildsTargetsAndVerifiesThemAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = CreatePaths(testDirectory.Path);
        LauncherContentVersion version = CreateVersion(ContentSourceKind.ManagedSingleFile);
        ContentIntegrityTarget target = CreateTarget("package", Path.Combine(paths.ModsDirectory, "ShockWave", "1.2"));
        LaunchContentIntegrityTargetContext[] contexts =
        [
            new LaunchContentIntegrityTargetContext(target, version, isCache: false),
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
        LauncherPaths paths = CreatePaths(testDirectory.Path);
        LauncherContentVersion version = CreateVersion(
            ContentSourceKind.ManagedSingleFile,
            imageSourceLink: imageSourceLink);
        ContentIntegrityTarget cacheTarget = CreateTarget(
            "cache",
            paths.GetModificationImagesDirectory("ShockWave"),
            ContentSourceKind.ManagedSingleFile);
        LaunchContentIntegrityTargetContext cacheContext = new(cacheTarget, version, isCache: true);
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
    public async Task ResolveAsync_RefreshesManagedCacheAndPreservesIgnoredFilesAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = CreatePaths(testDirectory.Path);
        LauncherContentVersion version = CreateVersion(ContentSourceKind.ManagedSingleFile);
        string cacheRoot = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(cacheRoot);
        string staleFilePath = Path.Combine(cacheRoot, "stale.txt");
        string ignoredFilePath = Path.Combine(cacheRoot, "ignored.txt");
        await File.WriteAllTextAsync(staleFilePath, "stale");
        await File.WriteAllTextAsync(ignoredFilePath, "ignored");

        ContentIntegrityTarget cacheTarget = CreateTarget(
            "cache",
            cacheRoot,
            ContentSourceKind.ManagedSingleFile,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ignored.txt" });
        LaunchContentIntegrityTargetContext cacheContext = new(cacheTarget, version, isCache: true);
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
        RecordingResolutionProgress progress = new();

        await service.ResolveAsync(
            new LaunchContentIntegrityResolutionRequest(paths, report, new[] { cacheContext }),
            progress,
            CancellationToken.None);

        File.Exists(staleFilePath).Should().BeFalse();
        File.Exists(ignoredFilePath).Should().BeTrue();
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
        LauncherPaths paths = CreatePaths(testDirectory.Path);
        LauncherContentVersion version = CreateVersion(ContentSourceKind.ManagedSingleFile);
        ContentIntegrityTarget packageTarget = CreateTarget(
            "package",
            Path.Combine(paths.ModsDirectory, "ShockWave", "1.2"),
            ContentSourceKind.ManagedSingleFile);
        LaunchContentIntegrityTargetContext packageContext = new(packageTarget, version, isCache: false);
        ContentIntegrityReport report = CreateReport(
            "package",
            ContentSourceKind.ManagedSingleFile,
            IntegrityIssueKind.MissingFile,
            IntegrityIssueAction.Repair);
        PackageUpdateProgress packageProgress = new(100, 40, 40, "package.zip");
        var singleFilePackageUpdater = new RecordingSingleFilePackageUpdater(packageProgress);
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(
            integrityService,
            singleFilePackageUpdater: singleFilePackageUpdater);
        RecordingResolutionProgress progress = new();

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
    public async Task ResolveAsync_RepairsManagedS3PackageFileInPlaceUsingManifestAndTargetRootAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = CreatePaths(testDirectory.Path);
        LauncherContentVersion version = CreateS3Version();
        ContentIntegrityTarget packageTarget = CreateTarget(
            "package",
            Path.Combine(paths.ModsDirectory, "ShockWave", "1.2"),
            ContentSourceKind.ManagedS3);
        LaunchContentIntegrityTargetContext packageContext = new(packageTarget, version, isCache: false);
        ContentIntegrityReport report = CreateReport(
            "package",
            ContentSourceKind.ManagedS3,
            IntegrityIssueKind.ModifiedFile,
            IntegrityIssueAction.Repair,
            "Data/file.gib");
        RemoteFileManifestEntry[] files =
        {
            new RemoteFileManifestEntry("Data/file.big", "0123456789ABCDEF0123456789ABCDEF", 10),
            new RemoteFileManifestEntry("Data/readme.txt", "0123456789ABCDEF0123456789ABCDEF", 5),
        };
        var manifestReader = new StubS3ObjectManifestReader(files);
        PackageUpdateProgress packageProgress = new(10, 10, 100, "Data/file.big");
        var s3PackageUpdater = new RecordingS3PackageUpdater(packageProgress);
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        FileSystemLaunchContentIntegrityResolutionService service = CreateService(
            integrityService,
            manifestReader: manifestReader,
            s3PackageUpdater: s3PackageUpdater);
        RecordingResolutionProgress progress = new();

        await service.ResolveAsync(
            new LaunchContentIntegrityResolutionRequest(paths, report, new[] { packageContext }),
            progress,
            CancellationToken.None);

        manifestReader.Requests.Should().ContainSingle().Which.Should().Match<S3ObjectManifestRequest>(request =>
            request.Endpoint == "https://s3.example.test" &&
            request.BucketName == "mods" &&
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
        LauncherPaths paths = CreatePaths(testDirectory.Path);
        LauncherContentVersion version = CreateS3Version();
        ContentIntegrityTarget packageTarget = CreateTarget(
            "package",
            Path.Combine(paths.ModsDirectory, "ShockWave", "1.2"),
            ContentSourceKind.ManagedS3);
        LaunchContentIntegrityTargetContext packageContext = new(packageTarget, version, isCache: false);
        ContentIntegrityReport report = CreateReport(
            "package",
            ContentSourceKind.ManagedS3,
            IntegrityIssueKind.Untracked,
            IntegrityIssueAction.Repair,
            ".");
        RemoteFileManifestEntry[] files =
        {
            new RemoteFileManifestEntry("Data/file.big", "0123456789ABCDEF0123456789ABCDEF", 10),
            new RemoteFileManifestEntry("Data/readme.txt", "0123456789ABCDEF0123456789ABCDEF", 5),
        };
        var manifestReader = new StubS3ObjectManifestReader(files);
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

    [Fact]
    public async Task RegisterManualImportAsync_MarksVersionManualAndCapturesPackageAndCacheSnapshotsAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = CreatePaths(testDirectory.Path);
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
            new LaunchContentIntegrityTargetContext(packageTarget, version, isCache: false),
            new LaunchContentIntegrityTargetContext(cacheTarget, version, isCache: true),
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
    public async Task CaptureManagedInstallSnapshotAsync_CapturesPackageAndRefreshesMismatchedImageCacheAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = CreatePaths(testDirectory.Path);
        LauncherContentVersion version = CreateVersion(ContentSourceKind.ManagedSingleFile);
        LauncherContentVersion inactiveVersion = CreateVersion(
            ContentSourceKind.ManagedSingleFile,
            version: "1.1");
        string cacheRoot = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(cacheRoot);
        string staleFilePath = Path.Combine(cacheRoot, "stale.txt");
        string inactiveImagePath = Path.Combine(cacheRoot, "1.1.jpg");
        await File.WriteAllTextAsync(staleFilePath, "stale");
        await File.WriteAllTextAsync(inactiveImagePath, "inactive");
        RecordingContentIntegrityService integrityService = CreateIntegrityService();
        var assetDownloader = new RecordingRemoteAssetDownloader();
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
        conditionalSnapshot.Target.IgnoredRelativePaths.Should().ContainSingle().Which.Should().Be("1.1.jpg");
        conditionalSnapshot.ExpectedRelativePaths.Should().BeEquivalentTo("1.2.jpg");

        File.Exists(staleFilePath).Should().BeFalse();
        File.Exists(inactiveImagePath).Should().BeTrue();
        assetDownloader.Calls.Should().BeEquivalentTo(new[]
        {
            (
                new Uri("https://cdn.example.test/card.jpg"),
                Path.Combine(cacheRoot, "1.2.jpg")),
        });
        integrityService.CapturedTargets.Should().HaveCount(2);
        integrityService.CapturedTargets[1].Should().Be(conditionalSnapshot.Target);
    }

    [Fact]
    public async Task CaptureManualImageSnapshotAsync_CapturesOnlyResolvedManualImageCacheAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = CreatePaths(testDirectory.Path);
        LauncherContentVersion version = CreateVersion(
            ContentSourceKind.Manual,
            simpleDownloadLink: string.Empty);
        LauncherContentVersion inactiveVersion = CreateVersion(
            ContentSourceKind.Manual,
            version: "1.1",
            simpleDownloadLink: string.Empty);
        string cacheRoot = paths.GetModificationImagesDirectory("ShockWave");
        Directory.CreateDirectory(cacheRoot);
        await File.WriteAllTextAsync(Path.Combine(cacheRoot, "1.1-background.png"), "inactive");
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
        cacheTarget.IgnoredRelativePaths.Should().ContainSingle().Which.Should().Be("1.1-background.png");
        integrityService.ConditionalSnapshotRequests.Should().BeEmpty();
        catalog.SaveCount.Should().Be(0);
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
            manifestReader ?? new StubS3ObjectManifestReader(),
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
        return new ContentIntegrityReport(new[]
        {
            new ContentIntegrityIssue(
                targetId,
                "ShockWave 1.2",
                sourceKind,
                issueKind,
                action,
                relativePath),
        });
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

    private static LauncherContentVersion CreateVersion(
        ContentSourceKind sourceKind,
        string version = "1.2",
        string simpleDownloadLink = "https://www.dropbox.com/s/package/file.zip?dl=0",
        string imageSourceLink = "https://cdn.example.test/card.jpg")
    {
        return new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { ContentSourceKind = sourceKind },
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = version,
            SimpleDownloadLink = simpleDownloadLink,
            UIImageSourceLink = imageSourceLink,
        };
    }

    private static LauncherContentVersion CreateS3Version()
    {
        return new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { ContentSourceKind = ContentSourceKind.ManagedS3 },
            ModificationType = ModificationType.Mod,
            Name = "ShockWave",
            Version = "1.2",
            S3HostLink = "https://s3.example.test",
            S3BucketName = "mods",
            S3FolderName = "ShockWave/1.2",
        };
    }

    private static LauncherPaths CreatePaths(string root)
    {
        return TestLauncherPaths.Create(Path.Combine(root, "Game"));
    }

    private sealed class RecordingResolutionProgress : IProgress<LaunchContentIntegrityResolutionProgress>
    {
        public List<LaunchContentIntegrityResolutionProgress> Reports { get; } = new();

        public void Report(LaunchContentIntegrityResolutionProgress value)
        {
            Reports.Add(value);
        }
    }

    private sealed class StubS3ObjectManifestReader : IS3ObjectManifestReader
    {
        private readonly IReadOnlyList<RemoteFileManifestEntry> _files;

        public StubS3ObjectManifestReader()
            : this(Array.Empty<RemoteFileManifestEntry>())
        {
        }

        public StubS3ObjectManifestReader(IReadOnlyList<RemoteFileManifestEntry> files)
        {
            _files = files;
        }

        public List<S3ObjectManifestRequest> Requests { get; } = new();

        public Task<IReadOnlyList<RemoteFileManifestEntry>> ReadManifestAsync(
            S3ObjectManifestRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_files);
        }
    }

    private sealed class RecordingS3PackageUpdater : IS3PackageUpdater
    {
        private readonly PackageUpdateProgress? _repairProgress;

        public RecordingS3PackageUpdater(PackageUpdateProgress? repairProgress = null)
        {
            _repairProgress = repairProgress;
        }

        public List<S3PackageUpdateRequest> UpdateRequests { get; } = new();

        public List<S3PackageFileRepairRequest> RepairRequests { get; } = new();

        public Task UpdateAsync(
            S3PackageUpdateRequest request,
            IProgress<PackageUpdateProgress>? progress,
            CancellationToken cancellationToken,
            PackageDownloadPauseController? pauseController = null)
        {
            UpdateRequests.Add(request);
            return Task.CompletedTask;
        }

        public Task RepairFilesAsync(
            S3PackageFileRepairRequest request,
            IProgress<PackageUpdateProgress>? progress,
            CancellationToken cancellationToken)
        {
            RepairRequests.Add(request);

            if (_repairProgress is not null)
            {
                progress?.Report(_repairProgress);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingContentIntegrityService : IContentIntegrityService
    {
        public ContentIntegrityReport VerificationReport { get; set; } =
            new(Array.Empty<ContentIntegrityIssue>());

        public bool CaptureIfMatchesExpectedFileSetResult { get; set; }

        public List<IReadOnlyList<ContentIntegrityTarget>> VerifiedTargetSets { get; } = new();

        public List<LauncherPaths> VerifiedPaths { get; } = new();

        public List<(
            ContentIntegrityTarget Target,
            IReadOnlySet<string> ExpectedRelativePaths)> ConditionalSnapshotRequests
        { get; } = new();

        public List<LauncherPaths> ConditionalSnapshotPaths { get; } = new();

        public List<ContentIntegrityTarget> CapturedTargets { get; } = new();

        public List<LauncherPaths> CapturedPaths { get; } = new();

        public Task<ContentIntegrityReport> VerifyAsync(
            LauncherPaths paths,
            IReadOnlyList<ContentIntegrityTarget> targets,
            CancellationToken cancellationToken)
        {
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
            ConditionalSnapshotPaths.Add(paths);
            ConditionalSnapshotRequests.Add((target, expectedRelativePaths));
            return Task.FromResult(CaptureIfMatchesExpectedFileSetResult);
        }

        public Task CaptureSnapshotAsync(
            LauncherPaths paths,
            ContentIntegrityTarget target,
            CancellationToken cancellationToken)
        {
            CapturedPaths.Add(paths);
            CapturedTargets.Add(target);
            return Task.CompletedTask;
        }

        public Task ApplyCleanupAsync(
            ContentIntegrityReport report,
            IReadOnlyList<ContentIntegrityTarget> targets,
            CancellationToken cancellationToken)
        {
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

        public List<LaunchContentIntegrityTargetRequest> Requests { get; } = new();

        public IReadOnlyList<LaunchContentIntegrityTargetContext> BuildTargets(
            LaunchContentIntegrityTargetRequest request)
        {
            Requests.Add(request);
            return _contexts;
        }
    }

    private sealed class RecordingSingleFilePackageUpdater : ISingleFilePackageUpdater
    {
        private readonly PackageUpdateProgress? _progressToReport;

        public RecordingSingleFilePackageUpdater(PackageUpdateProgress? progressToReport = null)
        {
            _progressToReport = progressToReport;
        }

        public List<(Uri SourceUri, PackageUpdatePathSet Paths)> Requests { get; } = new();

        public Task UpdateAsync(
            Uri sourceUri,
            PackageUpdatePathSet paths,
            IProgress<PackageUpdateProgress>? progress,
            CancellationToken cancellationToken,
            PackageDownloadPauseController? pauseController = null)
        {
            Requests.Add((sourceUri, paths));

            if (_progressToReport is not null)
            {
                progress?.Report(_progressToReport);
            }

            return Task.CompletedTask;
        }
    }
}
