using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Integrity.Models;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Mods.Services;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Services;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Services;

public sealed class PackageDownloadServiceTests
{
    [Fact]
    public async Task DownloadAsyncUsesLatestVersionLinkAndOwnedPathsForSingleFilePackageAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = CreatePaths(testDirectory.Path);
        LauncherContentVersion oldVersion = CreateSingleFileVersion(
            "ShockWave",
            "1.0",
            "https://example.test/old.zip");
        LauncherContentVersion latestVersion = CreateSingleFileVersion(
            "ShockWave",
            "2.0",
            "https://www.dropbox.com/s/package/latest.zip?dl=0");
        LauncherContent modification = CreateModification(oldVersion, latestVersion);
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
        request.Paths.InstalledPath.FullPath.Should().Be(Path.Combine(paths.ModsDirectory, "ShockWave", "2.0"));
        request.Paths.TemporaryPath.FullPath.Should()
            .Be(Path.Combine(paths.TempDirectory, "Packages", "ShockWave", "2.0"));
        request.Paths.BackupPath.FullPath.Should()
            .Be(Path.Combine(paths.StateDirectory, "PackageBackups", "ShockWave", "2.0"));
        request.Paths.InstalledPath.OwnerRoot.Should().Be(paths.ModsDirectory);
        request.Paths.TemporaryPath.OwnerRoot.Should().Be(
            Path.Combine(paths.TempDirectory, "Packages"));
        request.Paths.BackupPath.OwnerRoot.Should().Be(
            Path.Combine(paths.StateDirectory, "PackageBackups"));
        updater.PauseControllers.Should().ContainSingle().Which.Should().BeSameAs(pauseController);
    }

    [Fact]
    public async Task DownloadAsyncUsesNewGameStorageAfterRuntimeSwitchWithoutRebuildingServiceAsync()
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
        LauncherContentVersion version = CreateSingleFileVersion(
            "Shared Mod",
            "1.0",
            "https://example.test/shared.zip");
        LauncherContent modification = CreateModification(version);

        await service.DownloadAsync(modification, version, null, CancellationToken.None);
        runtimePaths.SwitchActive(zeroHourPaths);
        await service.DownloadAsync(modification, version, null, CancellationToken.None);

        updater.Requests.Select(request => request.Paths.InstalledPath.OwnerRoot)
            .Should().Equal(generalsPaths.ModsDirectory, zeroHourPaths.ModsDirectory);
        updater.Requests.Select(request => request.Paths.TemporaryPath.OwnerRoot)
            .Should().Equal(generalsPaths.PackagesDirectory, zeroHourPaths.PackagesDirectory);
    }

    [Fact]
    public async Task DownloadAsyncUsesS3ManifestAndLatestInstalledVersionPathAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = CreatePaths(testDirectory.Path);
        LauncherContentVersion installedVersion = CreateS3Version("ShockWave", "1.0");
        installedVersion.Installation.Installed = true;
        LauncherContentVersion latestVersion = CreateS3Version("ShockWave", "2.0");
        LauncherContent modification = CreateModification(installedVersion, latestVersion);
        RemoteFileManifestEntry[] manifestEntries =
        {
            new("Data/file.big", "0123456789ABCDEF0123456789ABCDEF", 10),
        };
        RecordingS3ObjectManifestReader manifestReader = new(manifestEntries);
        RecordingS3PackageUpdater updater = new();
        PackageDownloadService service = CreateService(
            paths,
            s3PackageUpdater: updater,
            manifestReader: manifestReader);

        PackageDownloadResult result = await service.DownloadAsync(
            modification,
            latestVersion,
            null,
            CancellationToken.None);

        result.Status.Should().Be(PackageDownloadStatus.Succeeded);
        S3ObjectManifestRequest manifestRequest = manifestReader.Requests.Should().ContainSingle().Which;
        manifestRequest.Endpoint.Should().Be("https://s3.example.test");
        manifestRequest.BucketName.Should().Be("mods");
        manifestRequest.Prefix.Should().Be("ShockWave/2.0");

        S3PackageUpdateRequest updateRequest = updater.Requests.Should().ContainSingle().Which;
        updateRequest.Files.Should().Equal(manifestEntries);
        updateRequest.Source.Should().BeSameAs(manifestRequest);
        updateRequest.PathSet.InstalledPath.FullPath.Should()
            .Be(Path.Combine(paths.ModsDirectory, "ShockWave", "2.0"));
        updateRequest.PathSet.LatestInstalledPath!.FullPath.Should()
            .Be(Path.Combine(paths.ModsDirectory, "ShockWave", "1.0"));
        updateRequest.HashCheckedExtensions.Should().Contain(".big");
        updateRequest.PathSet.InstalledPath.OwnerRoot.Should().Be(paths.ModsDirectory);
    }

    [Fact]
    public async Task DownloadAsyncReturnsCanceledWhenCancellationStopsPreCommitWorkAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = CreatePaths(testDirectory.Path);
        LauncherContentVersion version = CreateSingleFileVersion(
            "ShockWave",
            "2.0",
            "https://example.test/latest.zip");
        BlockingSingleFilePackageUpdater updater = new();
        PackageDownloadService service = CreateService(paths, updater);
        using CancellationTokenSource cancellation = new();

        Task<PackageDownloadResult> download = service.DownloadAsync(
            CreateModification(version),
            version,
            null,
            cancellation.Token);
        await updater.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        PackageDownloadResult result = await download.WaitAsync(TimeSpan.FromSeconds(5));

        result.Status.Should().Be(PackageDownloadStatus.Canceled);
    }

    [Fact]
    public async Task DownloadAsyncKeepsSuccessWhenCancellationArrivesAfterUpdaterCommitAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = CreatePaths(testDirectory.Path);
        LauncherContentVersion version = CreateSingleFileVersion(
            "ShockWave",
            "2.0",
            "https://example.test/latest.zip");
        using CancellationTokenSource cancellation = new();
        RecordingSingleFilePackageUpdater updater = new()
        {
            Update = (_, _, _) =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            },
        };
        PackageDownloadService service = CreateService(paths, updater);

        PackageDownloadResult result = await service.DownloadAsync(
            CreateModification(version),
            version,
            null,
            cancellation.Token);

        result.Status.Should().Be(PackageDownloadStatus.Succeeded);
    }

    [Fact]
    public async Task DownloadAsyncTreatsProviderFailureAfterCancellationAsCanceledAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = CreatePaths(testDirectory.Path);
        LauncherContentVersion version = CreateSingleFileVersion(
            "ShockWave",
            "2.0",
            "https://example.test/latest.zip");
        using CancellationTokenSource cancellation = new();
        RecordingSingleFilePackageUpdater updater = new()
        {
            Update = (_, _, _) =>
            {
                cancellation.Cancel();
                throw new IOException("provider aborted after cancellation");
            },
        };
        PackageDownloadService service = CreateService(paths, updater);

        PackageDownloadResult result = await service.DownloadAsync(
            CreateModification(version),
            version,
            null,
            cancellation.Token);

        result.Status.Should().Be(PackageDownloadStatus.Canceled);
    }

    [Theory]
    [MemberData(nameof(ExpectedFailureCases))]
    public async Task DownloadAsyncReturnsSafeRecoverableFailureForExpectedPackageFailuresAsync(
        Exception failure,
        string expectedMessage)
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = CreatePaths(testDirectory.Path);
        LauncherContentVersion version = CreateSingleFileVersion(
            "ShockWave",
            "2.0",
            "https://example.test/latest.zip");
        RecordingSingleFilePackageUpdater updater = new()
        {
            Update = (_, _, _) => throw failure,
        };
        PackageDownloadService service = CreateService(paths, updater);

        PackageDownloadResult result = await service.DownloadAsync(
            CreateModification(version),
            version,
            null,
            CancellationToken.None);

        result.Status.Should().Be(PackageDownloadStatus.RecoverableFailure);
        result.Message.Should().Be(expectedMessage);
        result.Message.Should().NotContain(@"C:\private");
    }

    [Fact]
    public async Task DownloadAsyncReturnsSafeUnexpectedFailureWithoutLeakingDiagnosticPathAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = CreatePaths(testDirectory.Path);
        LauncherContentVersion version = CreateSingleFileVersion(
            "ShockWave",
            "2.0",
            "https://example.test/latest.zip");
        RecordingSingleFilePackageUpdater updater = new()
        {
            Update = (_, _, _) => throw new InvalidOperationException(@"failure at C:\private\package"),
        };
        PackageDownloadService service = CreateService(paths, updater);

        PackageDownloadResult result = await service.DownloadAsync(
            CreateModification(version),
            version,
            null,
            CancellationToken.None);

        result.Status.Should().Be(PackageDownloadStatus.UnexpectedFailure);
        result.Message.Should().Be("An unexpected package download error occurred.");
    }

    [Fact]
    public async Task DownloadAsyncSerializesConcurrentProgressWithoutRegressionAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths paths = CreatePaths(testDirectory.Path);
        LauncherContentVersion version = CreateSingleFileVersion(
            "ShockWave",
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
            },
        };
        PackageDownloadService service = CreateService(paths, updater);
        ConcurrentQueue<PackageUpdateProgress> reports = new();

        PackageDownloadResult result = await service.DownloadAsync(
            CreateModification(version),
            version,
            new InlineRecordingProgress(reports),
            CancellationToken.None);

        result.Status.Should().Be(PackageDownloadStatus.Succeeded);
        PackageUpdateProgress[] delivered = reports.ToArray();
        delivered.Should().NotBeEmpty();
        delivered.Select(report => report.BytesRead)
            .Should().BeInAscendingOrder();
        delivered.Select(report => report.ProgressPercentage!.Value)
            .Should().BeInAscendingOrder()
            .And.OnlyContain(value => value >= 0 && value <= 100);
    }

    public static TheoryData<Exception, string> ExpectedFailureCases =>
        new()
        {
            {
                new TimeoutException("signed URL expired"),
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
        };

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

    private static LauncherPaths CreatePaths(string root)
    {
        return TestLauncherPaths.Create(Path.Combine(root, "Game"));
    }

    private static LauncherContentVersion CreateSingleFileVersion(
        string name,
        string version,
        string downloadLink)
    {
        return new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { ContentSourceKind = ContentSourceKind.ManagedSingleFile },
            ModificationType = ModificationType.Mod,
            Name = name,
            Version = version,
            SimpleDownloadLink = downloadLink,
        };
    }

    private static LauncherContentVersion CreateS3Version(string name, string version)
    {
        return new LauncherContentVersion
        {
            Installation = new LauncherContentInstallation { ContentSourceKind = ContentSourceKind.ManagedS3 },
            ModificationType = ModificationType.Mod,
            Name = name,
            Version = version,
            S3HostLink = "https://s3.example.test",
            S3BucketName = "mods",
            S3FolderName = $"{name}/{version}",
            S3HostPublicKey = "access-key",
            S3HostSecretKey = "secret-key",
        };
    }

    private static LauncherContent CreateModification(params LauncherContentVersion[] versions)
    {
        var data = new LauncherData();
        foreach (LauncherContentVersion version in versions)
        {
            data.AddOrUpdate(version);
        }

        return data.FindContent(versions[0].ContentKey)!;
    }

    private sealed class RecordingSingleFilePackageUpdater : ISingleFilePackageUpdater
    {
        public List<(Uri SourceUri, PackageUpdatePathSet Paths)> Requests { get; } = new();

        public List<PackageDownloadPauseController?> PauseControllers { get; } = new();

        public Func<
            Uri,
            IProgress<PackageUpdateProgress>?,
            CancellationToken,
            Task>? Update
        { get; init; }

        public Task UpdateAsync(
            Uri sourceUri,
            PackageUpdatePathSet paths,
            IProgress<PackageUpdateProgress>? progress,
            CancellationToken cancellationToken,
            PackageDownloadPauseController? pauseController = null)
        {
            Requests.Add((sourceUri, paths));
            PauseControllers.Add(pauseController);
            return Update?.Invoke(sourceUri, progress, cancellationToken) ?? Task.CompletedTask;
        }
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

    private sealed class RecordingS3PackageUpdater : IS3PackageUpdater
    {
        public List<S3PackageUpdateRequest> Requests { get; } = new();

        public Task UpdateAsync(
            S3PackageUpdateRequest request,
            IProgress<PackageUpdateProgress>? progress,
            CancellationToken cancellationToken,
            PackageDownloadPauseController? pauseController = null)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }

        public Task RepairFilesAsync(
            S3PackageFileRepairRequest request,
            IProgress<PackageUpdateProgress>? progress,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingS3ObjectManifestReader : IS3ObjectManifestReader
    {
        private readonly IReadOnlyList<RemoteFileManifestEntry> _files;

        public RecordingS3ObjectManifestReader()
            : this(Array.Empty<RemoteFileManifestEntry>())
        {
        }

        public RecordingS3ObjectManifestReader(IReadOnlyList<RemoteFileManifestEntry> files)
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

    private sealed class InlineRecordingProgress : IProgress<PackageUpdateProgress>
    {
        private readonly ConcurrentQueue<PackageUpdateProgress> _reports;

        public InlineRecordingProgress(ConcurrentQueue<PackageUpdateProgress> reports)
        {
            _reports = reports;
        }

        public void Report(PackageUpdateProgress value)
        {
            _reports.Enqueue(value);
        }
    }
}
