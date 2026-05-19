using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Minio.Exceptions;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Services;

public sealed class PackageDownloadServiceTests
{
    private static readonly string _shockWave20 = Path.Combine("ShockWave", "2.0");

    private static readonly string _shockWave30 = Path.Combine("ShockWave", "3.0");

    public static TheoryData<Exception, string> ExpectedFailureCases =>
        new()
        {
            {
                new TimeoutException("signed URL expired"),
                "The remote package provider could not complete the download."
            },
            {
                new UnexpectedMinioException("presign failed"),
                "The remote package provider could not complete the download."
            },
            {
                new HttpRequestException("offline"),
                "The remote package provider could not complete the download."
            },
            {
                new InvalidDataException(@"bad archive at C:\private\package.zip"),
                "The downloaded package could not be validated."
            },
            {
                new IOException(@"install failed at C:\private\package"),
                "The package could not be staged or installed in launcher storage."
            },
            {
                new UnauthorizedAccessException(@"denied at C:\private\package"),
                "The package could not be staged or installed in launcher storage."
            }
        };

    [Fact]
    public async Task DownloadAsync_UsesLatestVersionLinkAndOwnedPathsForSingleFilePackageAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion oldVersion = CreateSingleFileVersion(
            "1.0",
            "https://example.test/old.zip");
        LauncherContentVersion latestVersion = CreateSingleFileVersion(
            "2.0",
            "https://www.dropbox.com/s/package/latest.zip?dl=0");
        LauncherContent modification = TestLauncherContent.From(oldVersion, latestVersion);
        RecordingSingleFilePackageUpdater updater = new();
        PackageDownloadService service = CreateService(paths, updater);
        PackageDownloadPauseController pauseController = new();

        PackageDownloadResult result = await service.DownloadAsync(
            modification,
            latestVersion,
            null,
            CancellationToken.None,
            pauseController);

        result.Status.Should().Be(PackageDownloadStatus.Succeeded);
        (Uri SourceUri, PackageUpdatePathSet Paths) request = updater.Requests.Should().ContainSingle().Which;
        request.SourceUri.Should().Be(new Uri("https://www.dropbox.com/s/package/latest.zip?dl=1"));
        request.Paths.Should().Be(TestPackageUpdatePaths.Create(paths, _shockWave20, _shockWave20));
        updater.PauseControllers.Should().ContainSingle().Which.Should().BeSameAs(pauseController);
    }

    [Fact]
    public async Task DownloadAsync_UsesNewGameStorageAfterRuntimeSwitchWithoutRebuildingServiceAsync()
    {
        using TestDirectory directory = new();
        string executableDirectory = directory.CreateDirectory("Launcher");
        var storagePaths = new LauncherStoragePaths(executableDirectory);
        LauncherPaths generalsPaths = storagePaths.CreateGamePaths(
            SupportedGame.Generals,
            directory.CreateDirectory("GeneralsGame"));
        LauncherPaths zeroHourPaths = storagePaths.CreateGamePaths(
            SupportedGame.ZeroHour,
            directory.CreateDirectory("ZeroHourGame"));
        var runtimePaths = new LauncherRuntimePathContext(storagePaths, generalsPaths);
        var updater = new RecordingSingleFilePackageUpdater();
        PackageDownloadService service = CreateService(runtimePaths, updater);
        LauncherContentVersion version = TestLauncherContent.Version(
            "Shared Mod",
            sourceKind: ContentSourceKind.ManagedSingleFile,
            simpleDownloadLink: "https://example.test/shared.zip");
        LauncherContent modification = TestLauncherContent.From(version);

        await service.DownloadAsync(modification, version, null, CancellationToken.None);
        runtimePaths.SwitchActive(zeroHourPaths);
        await service.DownloadAsync(modification, version, null, CancellationToken.None);

        updater.Requests.Select(request => request.Paths.InstalledPath.OwnerRoot)
            .Should().Equal(generalsPaths.ModsDirectory, zeroHourPaths.ModsDirectory);
        updater.Requests.Select(request => request.Paths.TemporaryPath.OwnerRoot)
            .Should().Equal(generalsPaths.PackagesDirectory, zeroHourPaths.PackagesDirectory);
    }

    /// <summary>
    ///     Reuse comes from the newest installed version, not the newest listed one, so a package with several installed
    ///     versions still copies from the closest predecessor rather than the oldest install.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_UsesS3ManifestAndLatestInstalledVersionPathAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContent modification = TestLauncherContent.From(
            TestLauncherContent.S3Version("ShockWave", "1.0", installed: true),
            TestLauncherContent.S3Version("ShockWave", "2.0", installed: true),
            TestLauncherContent.S3Version("ShockWave", "3.0"));
        LauncherContentVersion targetVersion = modification.Versions
            .Single(version => version.Version == "3.0");
        RemoteFileManifestEntry[] manifestEntries =
        [
            new("Data/file.big", StubFileHashService.MatchingHash, 10)
        ];
        RecordingS3ObjectManifestReader manifestReader = new();
        manifestReader.Enqueue(manifestEntries);
        RecordingS3PackageUpdater updater = new();
        PackageDownloadService service = CreateService(
            paths,
            s3PackageUpdater: updater,
            manifestReader: manifestReader);

        PackageDownloadResult result = await service.DownloadAsync(
            modification,
            targetVersion,
            null,
            CancellationToken.None);

        result.Status.Should().Be(PackageDownloadStatus.Succeeded);
        S3ObjectManifestRequest manifestRequest = manifestReader.Requests.Should().ContainSingle().Which;
        manifestRequest.Endpoint.Should().Be(TestLauncherContent.S3Host);
        manifestRequest.BucketName.Should().Be(TestLauncherContent.S3Bucket);
        manifestRequest.Prefix.Should().Be("ShockWave/3.0");

        S3PackageUpdateRequest updateRequest = updater.UpdateRequests.Should().ContainSingle().Which;
        updateRequest.Files.Should().Equal(manifestEntries);
        updateRequest.Source.Should().BeSameAs(manifestRequest);
        updateRequest.PathSet.Should().Be(
            TestPackageUpdatePaths.Create(paths, _shockWave30, _shockWave30, _shockWave20));
    }

    /// <summary>
    ///     Without an installed predecessor there is nothing to copy from, so the updater must be told so rather than
    ///     handed a folder that does not exist.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_NoInstalledVersion_OmitsLatestInstalledPathAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion targetVersion = TestLauncherContent.S3Version("ShockWave", "3.0");
        LauncherContent modification = TestLauncherContent.From(targetVersion);
        RecordingS3PackageUpdater updater = new();
        PackageDownloadService service = CreateService(paths, s3PackageUpdater: updater);

        await service.DownloadAsync(modification, targetVersion, null, CancellationToken.None);

        updater.UpdateRequests.Should().ContainSingle()
            .Which.PathSet.LatestInstalledPath.Should().BeNull();
    }

    /// <summary>
    ///     Only launcher-managed file kinds carry a reliable MD5 in the remote manifest; hashing anything else would
    ///     reject files the backend never promised a checksum for.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_RequestsHashValidationForManagedGameContentExtensionsAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion targetVersion = TestLauncherContent.S3Version("ShockWave", "3.0");
        LauncherContent modification = TestLauncherContent.From(targetVersion);
        RecordingS3PackageUpdater updater = new();
        PackageDownloadService service = CreateService(paths, s3PackageUpdater: updater);

        await service.DownloadAsync(modification, targetVersion, null, CancellationToken.None);

        updater.UpdateRequests.Should().ContainSingle()
            .Which.HashCheckedExtensions.Should().BeEquivalentTo(
                ".w3d",
                LauncherContentFileTypes.BigExtension,
                ".bik",
                LauncherContentFileTypes.GibExtension,
                ".dds",
                ".tga",
                ".ini",
                ".scb",
                ".wnd",
                ".csf",
                ".str");
    }

    [Fact]
    public async Task DownloadAsync_ReturnsCanceledWhenCancellationStopsPreCommitWorkAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateSingleFileVersion(
            "2.0",
            "https://example.test/latest.zip");
        BlockingSingleFilePackageUpdater updater = new();
        PackageDownloadService service = CreateService(paths, updater);
        using CancellationTokenSource cancellation = new();

        Task<PackageDownloadResult> download = service.DownloadAsync(
            TestLauncherContent.From(version),
            version,
            null,
            cancellation.Token);
        await updater.Started.Task.WaitAsync(TestTimeouts.Wait);
        cancellation.Cancel();
        PackageDownloadResult result = await download.WaitAsync(TestTimeouts.Wait);

        result.Status.Should().Be(PackageDownloadStatus.Canceled);
    }

    [Fact]
    public async Task DownloadAsync_KeepsSuccessWhenCancellationArrivesAfterUpdaterCommitAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateSingleFileVersion(
            "2.0",
            "https://example.test/latest.zip");
        using CancellationTokenSource cancellation = new();
        RecordingSingleFilePackageUpdater updater = new()
        {
            Update = (_, _, _) =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            }
        };
        PackageDownloadService service = CreateService(paths, updater);

        PackageDownloadResult result = await service.DownloadAsync(
            TestLauncherContent.From(version),
            version,
            null,
            cancellation.Token);

        result.Status.Should().Be(PackageDownloadStatus.Succeeded);
    }

    [Fact]
    public async Task DownloadAsync_TreatsProviderFailureAfterCancellationAsCanceledAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateSingleFileVersion(
            "2.0",
            "https://example.test/latest.zip");
        using CancellationTokenSource cancellation = new();
        RecordingSingleFilePackageUpdater updater = new()
        {
            Update = (_, _, _) =>
            {
                cancellation.Cancel();
                throw new IOException("provider aborted after cancellation");
            }
        };
        PackageDownloadService service = CreateService(paths, updater);

        PackageDownloadResult result = await service.DownloadAsync(
            TestLauncherContent.From(version),
            version,
            null,
            cancellation.Token);

        result.Status.Should().Be(PackageDownloadStatus.Canceled);
    }

    [Theory]
    // The cases carry Exception instances, which the runner cannot serialize into
    // individual rows, so discovery keeps them as one theory rather than enumerating.
    [MemberData(nameof(ExpectedFailureCases), DisableDiscoveryEnumeration = true)]
    public async Task DownloadAsync_ReturnsSafeRecoverableFailureForExpectedPackageFailuresAsync(
        Exception failure,
        string expectedMessage)
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateSingleFileVersion(
            "2.0",
            "https://example.test/latest.zip");
        RecordingSingleFilePackageUpdater updater = new()
        {
            Update = (_, _, _) => throw failure
        };
        PackageDownloadService service = CreateService(paths, updater);

        PackageDownloadResult result = await service.DownloadAsync(
            TestLauncherContent.From(version),
            version,
            null,
            CancellationToken.None);

        result.Status.Should().Be(PackageDownloadStatus.RecoverableFailure);
        result.Message.Should().Be(expectedMessage);
        result.Message.Should().NotContain(@"C:\private");
    }

    [Fact]
    public async Task DownloadAsync_ReturnsSafeUnexpectedFailureWithoutLeakingDiagnosticPathAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateSingleFileVersion(
            "2.0",
            "https://example.test/latest.zip");
        RecordingSingleFilePackageUpdater updater = new()
        {
            Update = (_, _, _) => throw new InvalidOperationException(@"failure at C:\private\package")
        };
        PackageDownloadService service = CreateService(paths, updater);

        PackageDownloadResult result = await service.DownloadAsync(
            TestLauncherContent.From(version),
            version,
            null,
            CancellationToken.None);

        result.Status.Should().Be(PackageDownloadStatus.UnexpectedFailure);
        result.Message.Should().Be("An unexpected package download error occurred.");
    }

    [Fact]
    public async Task DownloadAsync_SerializesConcurrentProgressWithoutRegressionAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = TestLauncherPaths.Create(testDirectory);
        LauncherContentVersion version = CreateSingleFileVersion(
            "2.0",
            "https://example.test/latest.zip");
        RecordingSingleFilePackageUpdater updater = new()
        {
            Update = async (_, progress, _) =>
            {
                await Task.WhenAll(
                    Task.Run(() => progress!.Report(new PackageUpdateProgress(100, 80, 80, "a"))),
                    Task.Run(() => progress!.Report(new PackageUpdateProgress(100, 20, 20, "b"))),
                    Task.Run(() => progress!.Report(new PackageUpdateProgress(100, 120, 120, "c"))));
            }
        };
        PackageDownloadService service = CreateService(paths, updater);
        RecordingProgress<PackageUpdateProgress> progress = new();

        PackageDownloadResult result = await service.DownloadAsync(
            TestLauncherContent.From(version),
            version,
            progress,
            CancellationToken.None);

        result.Status.Should().Be(PackageDownloadStatus.Succeeded);
        progress.Reports.Should().NotBeEmpty();
        progress.Reports.Select(report => report.BytesRead)
            .Should().BeInAscendingOrder();
        progress.Reports.Select(report => report.ProgressPercentage!.Value)
            .Should().BeInAscendingOrder()
            .And.OnlyContain(value => value >= 0 && value <= 100);
    }

    private static PackageDownloadService CreateService(
        LauncherPaths paths,
        ISingleFilePackageUpdater? singleFilePackageUpdater = null,
        IS3PackageUpdater? s3PackageUpdater = null,
        IS3ObjectManifestReader? manifestReader = null)
    {
        return CreateService(
            TestLauncherPaths.CreateRuntimePathContext(paths),
            singleFilePackageUpdater,
            s3PackageUpdater,
            manifestReader);
    }

    private static PackageDownloadService CreateService(
        LauncherRuntimePathContext runtimePathContext,
        ISingleFilePackageUpdater? singleFilePackageUpdater = null,
        IS3PackageUpdater? s3PackageUpdater = null,
        IS3ObjectManifestReader? manifestReader = null)
    {
        return new PackageDownloadService(
            singleFilePackageUpdater ?? new RecordingSingleFilePackageUpdater(),
            s3PackageUpdater ?? new RecordingS3PackageUpdater(),
            manifestReader ?? new RecordingS3ObjectManifestReader(),
            runtimePathContext,
            NullLogger<PackageDownloadService>.Instance);
    }

    private static LauncherContentVersion CreateSingleFileVersion(string version, string downloadLink)
    {
        return TestLauncherContent.Version(
            "ShockWave",
            version,
            sourceKind: ContentSourceKind.ManagedSingleFile,
            simpleDownloadLink: downloadLink);
    }

    private sealed class BlockingSingleFilePackageUpdater : ISingleFilePackageUpdater
    {
        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task UpdateAsync(
            Uri sourceUri,
            PackageUpdatePathSet paths,
            IProgress<PackageUpdateProgress>? progress,
            CancellationToken cancellationToken,
            PackageDownloadPauseController? pauseController = null)
        {
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
