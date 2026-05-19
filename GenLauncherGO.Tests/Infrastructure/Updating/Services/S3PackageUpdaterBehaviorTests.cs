using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Services;
using GenLauncherGO.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Services;

public sealed class S3PackageUpdaterBehaviorTests
{
    [Fact]
    public async Task UpdateAsync_CopiesMatchingFilesFromLatestAndSkipsDownloadAsync()
    {
        using TestDirectory testDirectory = new();
        string latestPath = Path.Combine(testDirectory.Path, "Mods", "latest");
        string temporaryPath = Path.Combine(testDirectory.Path, "Staging", "temp");
        string installedPath = Path.Combine(testDirectory.Path, "Mods", "installed");
        string latestFilePath = Path.Combine(latestPath, "Data", "readme.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(latestFilePath)!);
        await File.WriteAllTextAsync(latestFilePath, "payload");

        string hash = "0123456789ABCDEF0123456789ABCDEF";
        RecordingFileDownloader downloader = new();
        StubFileHashService hashService = new() { HashForPath = _ => hash };
        S3PackageUpdater updater = CreateUpdater(downloader, hashService);
        RecordingProgress progress = new();

        await updater.UpdateAsync(
            CreateRequest(
                temporaryPath,
                installedPath,
                latestPath,
                new RemoteFileManifestEntry("Data/readme.txt", hash, (ulong)new FileInfo(latestFilePath).Length)),
            progress,
            CancellationToken.None);

        downloader.Requests.Should().BeEmpty();
        File.ReadAllText(Path.Combine(installedPath, "Data", "readme.txt")).Should().Be("payload");
        progress.Reports.Should().Contain(report => report.FileName == null);
    }

    [Fact]
    public async Task UpdateAsync_ReusesInstalledGibVariantForBigManifestEntryAsync()
    {
        using TestDirectory testDirectory = new();
        string latestPath = Path.Combine(testDirectory.Path, "Mods", "latest");
        string temporaryPath = Path.Combine(testDirectory.Path, "Staging", "temp");
        string installedPath = Path.Combine(testDirectory.Path, "Mods", "installed");
        string latestFilePath = Path.Combine(latestPath, "Data", "archive.gib");
        Directory.CreateDirectory(Path.GetDirectoryName(latestFilePath)!);
        await File.WriteAllTextAsync(latestFilePath, "payload");

        const string Hash = "0123456789ABCDEF0123456789ABCDEF";
        RecordingFileDownloader downloader = new();
        S3PackageUpdater updater = CreateUpdater(
            downloader,
            new StubFileHashService { HashForPath = _ => Hash });

        await updater.UpdateAsync(
            CreateRequest(
                temporaryPath,
                installedPath,
                latestPath,
                new RemoteFileManifestEntry(
                    "Data/archive.big",
                    Hash,
                    (ulong)new FileInfo(latestFilePath).Length)),
            null,
            CancellationToken.None);

        downloader.Requests.Should().BeEmpty();
        File.ReadAllText(Path.Combine(installedPath, "Data", "archive.gib")).Should().Be("payload");
        File.Exists(Path.Combine(installedPath, "Data", "archive.big")).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_ReportsOnlyMissingFileBytesWhenLatestFilesAreReusedAsync()
    {
        using TestDirectory testDirectory = new();
        string latestPath = Path.Combine(testDirectory.Path, "Mods", "latest");
        string temporaryPath = Path.Combine(testDirectory.Path, "Staging", "temp");
        string installedPath = Path.Combine(testDirectory.Path, "Mods", "installed");
        string latestFilePath = Path.Combine(latestPath, "Data", "reused.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(latestFilePath)!);
        await File.WriteAllBytesAsync(latestFilePath, CreatePayload(660));

        string hash = "0123456789ABCDEF0123456789ABCDEF";
        RecordingFileDownloader downloader = new();
        StubFileHashService hashService = new() { HashForPath = _ => hash };
        S3PackageUpdater updater = CreateUpdater(downloader, hashService);
        RecordingProgress progress = new();

        await updater.UpdateAsync(
            CreateRequest(
                temporaryPath,
                installedPath,
                latestPath,
                new RemoteFileManifestEntry("Data/reused.txt", hash, 660),
                new RemoteFileManifestEntry("Data/missing.txt", hash, 20)),
            progress,
            CancellationToken.None);

        downloader.Requests.Should().ContainSingle();
        progress.Reports.Should().ContainSingle();
        progress.Reports[0].TotalBytes.Should().Be(20);
        progress.Reports[0].BytesRead.Should().Be(20);
        progress.Reports[0].ProgressPercentage.Should().Be(100);
    }

    [Fact]
    public async Task UpdateAsync_ReportsOnlyRemainingBytesForPartialStagedDownloadAsync()
    {
        using TestDirectory testDirectory = new();
        string temporaryPath = Path.Combine(testDirectory.Path, "Staging", "temp");
        string installedPath = Path.Combine(testDirectory.Path, "Mods", "installed");
        string partialFilePath = Path.Combine(temporaryPath, "Data", "missing.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(partialFilePath)!);
        await File.WriteAllBytesAsync(partialFilePath, CreatePayload(5));

        string hash = "0123456789ABCDEF0123456789ABCDEF";
        RecordingFileDownloader downloader = new();
        StubFileHashService hashService = new() { HashForPath = _ => hash };
        S3PackageUpdater updater = CreateUpdater(downloader, hashService);
        RecordingProgress progress = new();

        await updater.UpdateAsync(
            CreateRequest(
                temporaryPath,
                installedPath,
                null,
                new RemoteFileManifestEntry("Data/missing.txt", hash, 20)),
            progress,
            CancellationToken.None);

        downloader.Requests.Should().ContainSingle();
        progress.Reports.Should().ContainSingle();
        progress.Reports[0].TotalBytes.Should().Be(15);
        progress.Reports[0].BytesRead.Should().Be(15);
        progress.Reports[0].ProgressPercentage.Should().Be(100);
    }

    [Fact]
    public async Task UpdateAsync_RejectsManifestPathOutsideTemporaryFolderAsync()
    {
        using TestDirectory testDirectory = new();
        S3PackageUpdater updater = CreateUpdater(new RecordingFileDownloader(), new StubFileHashService());

        Func<Task> act = async () => await updater.UpdateAsync(
            CreateRequest(
                Path.Combine(testDirectory.Path, "Staging", "temp"),
                Path.Combine(testDirectory.Path, "Mods", "installed"),
                null,
                new RemoteFileManifestEntry("../escape.txt", "0123456789ABCDEF0123456789ABCDEF", 1)),
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        File.Exists(Path.Combine(testDirectory.Path, "escape.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_PrunesStaleTemporaryFilesBeforeInstallingAsync()
    {
        using TestDirectory testDirectory = new();
        string temporaryRoot = Path.Combine(testDirectory.Path, "Runtime", "Temp");
        string packagesPath = Path.Combine(temporaryRoot, "Packages");
        string temporaryPath = Path.Combine(packagesPath, "NProject Mod", "2.11");
        string installedPath = Path.Combine(testDirectory.Path, "Mods", "installed");
        Directory.CreateDirectory(temporaryPath);
        await File.WriteAllTextAsync(Path.Combine(temporaryPath, "stale.txt"), "stale");
        await File.WriteAllTextAsync(Path.Combine(temporaryPath, "readme.txt"), "payload");

        string hash = "0123456789ABCDEF0123456789ABCDEF";
        S3PackageUpdater updater = CreateUpdater(
            new RecordingFileDownloader(),
            new StubFileHashService { HashForPath = _ => hash });

        await updater.UpdateAsync(
            CreateRequest(
                temporaryPath,
                installedPath,
                null,
                new RemoteFileManifestEntry("readme.txt", hash, 7)),
            null,
            CancellationToken.None);

        File.Exists(Path.Combine(installedPath, "stale.txt")).Should().BeFalse();
        File.ReadAllText(Path.Combine(installedPath, "readme.txt")).Should().Be("payload");
        Directory.Exists(packagesPath).Should().BeFalse();
        Directory.Exists(temporaryRoot).Should().BeTrue();
    }

    [SymbolicLinkFact]
    public async Task UpdateAsync_RemovesUnsafeStagingLinkWithoutDeletingTargetAsync()
    {
        using TestDirectory testDirectory = new();
        string temporaryPath = Path.Combine(testDirectory.Path, "Staging", "temp");
        string installedPath = Path.Combine(testDirectory.Path, "Mods", "installed");
        string outsidePath = Path.Combine(testDirectory.Path, "outside");
        Directory.CreateDirectory(temporaryPath);
        Directory.CreateDirectory(outsidePath);
        await File.WriteAllTextAsync(Path.Combine(temporaryPath, "readme.txt"), "payload");
        string outsideFile = Path.Combine(outsidePath, "outside.txt");
        await File.WriteAllTextAsync(outsideFile, "outside");
        SymbolicLinkTestSupport.CreateDirectoryLink(
            Path.Combine(temporaryPath, "linked"),
            outsidePath);

        string hash = "0123456789ABCDEF0123456789ABCDEF";
        S3PackageUpdater updater = CreateUpdater(
            new RecordingFileDownloader(),
            new StubFileHashService { HashForPath = _ => hash });

        await updater.UpdateAsync(
            CreateRequest(
                temporaryPath,
                installedPath,
                null,
                new RemoteFileManifestEntry("readme.txt", hash, 7)),
            null,
            CancellationToken.None);

        Directory.Exists(Path.Combine(installedPath, "linked")).Should().BeFalse();
        File.ReadAllText(outsideFile).Should().Be("outside");
    }

    [Fact]
    public async Task RepairFilesAsync_DownloadsSelectedModifiedFileInPlaceAsync()
    {
        using TestDirectory testDirectory = new();
        string installedPath = Path.Combine(testDirectory.Path, "Mods", "installed");
        string staleFilePath = Path.Combine(installedPath, "Data", "readme.txt");
        string keepFilePath = Path.Combine(installedPath, "Data", "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(staleFilePath)!);
        await File.WriteAllTextAsync(staleFilePath, "stale");
        await File.WriteAllTextAsync(keepFilePath, "keep");

        string hash = "0123456789ABCDEF0123456789ABCDEF";
        RecordingFileDownloader downloader = new();
        StubFileHashService hashService = new()
        {
            HashForPath = path => File.ReadAllBytes(path).All(value => value == (byte)'x') ? hash : "BAD",
        };
        S3PackageUpdater updater = CreateUpdater(downloader, hashService);
        RecordingProgress progress = new();

        await updater.RepairFilesAsync(
            CreateRepairRequest(
                installedPath,
                new RemoteFileManifestEntry("Data/readme.txt", hash, 5)),
            progress,
            CancellationToken.None);

        DownloadFileRequest request = downloader.Requests.Should().ContainSingle().Which;
        request.DestinationFilePath.Should().Be(staleFilePath);
        File.ReadAllBytes(staleFilePath).Should().AllBeEquivalentTo((byte)'x');
        File.ReadAllText(keepFilePath).Should().Be("keep");
        progress.Reports.Should().ContainSingle(report =>
            report.TotalBytes == 5 &&
            report.BytesRead == 5 &&
            report.ProgressPercentage == 100);
    }

    [Fact]
    public async Task UpdateAsyncHashRetryReportsMonotonicProgressAndOnlyFinishesAtOneHundredAsync()
    {
        using TestDirectory testDirectory = new();
        string expectedHash = "0123456789ABCDEF0123456789ABCDEF";
        int hashAttempt = 0;
        S3PackageUpdater updater = CreateUpdater(
            new ProgressReportingFileDownloader(),
            new StubFileHashService
            {
                HashForPath = _ => Interlocked.Increment(ref hashAttempt) == 1
                    ? "BAD"
                    : expectedHash,
            });
        RecordingProgress progress = new();

        await updater.UpdateAsync(
            CreateRequest(
                Path.Combine(testDirectory.Path, "Staging", "temporary"),
                Path.Combine(testDirectory.Path, "Mods", "installed"),
                null,
                new RemoteFileManifestEntry("Data/file.txt", expectedHash, 5)),
            progress,
            CancellationToken.None);

        progress.Reports.Should().HaveCountGreaterThan(1);
        progress.Reports.Select(report => report.ProgressPercentage!.Value)
            .Should().BeInAscendingOrder();
        progress.Reports.Take(progress.Reports.Count - 1)
            .Should().OnlyContain(report => report.ProgressPercentage < 100);
        progress.Reports[^1].ProgressPercentage.Should().Be(100);
        progress.Reports[^1].BytesRead.Should().Be(progress.Reports[^1].TotalBytes);
    }

    [Theory]
    [InlineData("Data/file.big", "data/file.gib")]
    [InlineData("Data/file.txt", @"data\file.txt")]
    public async Task UpdateAsyncRejectsDuplicateNormalizedManifestDestinationsAsync(
        string firstFile,
        string secondFile)
    {
        using TestDirectory testDirectory = new();
        S3PackageUpdater updater = CreateUpdater(
            new RecordingFileDownloader(),
            new StubFileHashService());
        S3PackageUpdateRequest request = CreateRequest(
            Path.Combine(testDirectory.Path, "Staging", "temporary"),
            Path.Combine(testDirectory.Path, "Mods", "installed"),
            null,
            new RemoteFileManifestEntry(firstFile, string.Empty, 1),
            new RemoteFileManifestEntry(secondFile, string.Empty, 1));

        Func<Task> update = () => updater.UpdateAsync(
            request,
            null,
            CancellationToken.None);

        await update.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*duplicate local file destinations*");
    }

    private static S3PackageUpdater CreateUpdater(
        IResumableFileDownloader downloader,
        IFileHashService hashService)
    {
        return new S3PackageUpdater(
            downloader,
            hashService,
            NullLogger<S3PackageUpdater>.Instance);
    }

    private static S3PackageUpdateRequest CreateRequest(
        string temporaryPath,
        string installedPath,
        string? latestPath,
        params RemoteFileManifestEntry[] files)
    {
        string installedRoot = Path.GetDirectoryName(installedPath)!;
        string ownedGameDataRoot = Path.GetDirectoryName(installedRoot)!;
        string packageRoot = Path.Combine(
            ownedGameDataRoot,
            "Runtime",
            "Temp",
            LauncherFileSystemLayout.PackagesFolderName);
        string temporaryOwnerRoot = temporaryPath.StartsWith(
            packageRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)
            ? packageRoot
            : Path.Combine(ownedGameDataRoot, "Staging");
        string backupRoot = Path.Combine(ownedGameDataRoot, "Runtime", "State", "PackageBackups");
        string backupPath = Path.Combine(
            backupRoot,
            Path.GetRelativePath(installedRoot, installedPath));
        return new S3PackageUpdateRequest(
            files,
            CreateSource(),
            new PackageUpdatePathSet(
                new OwnedContentPath(temporaryOwnerRoot, temporaryPath),
                new OwnedContentPath(installedRoot, installedPath),
                new OwnedContentPath(backupRoot, backupPath),
                latestPath is null ? null : new OwnedContentPath(installedRoot, latestPath)),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt", ".big", ".gib" });
    }

    private static S3PackageFileRepairRequest CreateRepairRequest(
        string installedPath,
        params RemoteFileManifestEntry[] files)
    {
        return new S3PackageFileRepairRequest(
            files,
            CreateSource(),
            new OwnedContentPath(Path.GetDirectoryName(installedPath)!, installedPath),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt", ".big", ".gib" });
    }

    private static S3ObjectManifestRequest CreateSource()
    {
        return new S3ObjectManifestRequest(
            "https://example.test",
            "mods",
            "folder",
            "access",
            "secret");
    }

    private static byte[] CreatePayload(int length)
    {
        byte[] payload = new byte[length];
        Array.Fill(payload, (byte)'x');
        return payload;
    }

    private sealed class RecordingFileDownloader : IResumableFileDownloader
    {
        public ConcurrentQueue<DownloadFileRequest> Requests { get; } = new();

        public async Task DownloadFileAsync(
            DownloadFileRequest request,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            Requests.Enqueue(request);
            long length = request.ExpectedBytes.GetValueOrDefault();
            byte[] payload = CreatePayload(checked((int)length));
            Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationFilePath)!);
            await File.WriteAllBytesAsync(request.DestinationFilePath, payload, cancellationToken);
        }
    }

    private sealed class StubFileHashService : IFileHashService
    {
        public Func<string, string> HashForPath { get; init; } = _ => "0123456789ABCDEF0123456789ABCDEF";

        public Task<string> ComputeMd5HashAsync(string filePath, CancellationToken cancellationToken)
        {
            return Task.FromResult(HashForPath(filePath));
        }
    }

    private sealed class ProgressReportingFileDownloader : IResumableFileDownloader
    {
        public async Task DownloadFileAsync(
            DownloadFileRequest request,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            long length = request.ExpectedBytes.GetValueOrDefault();
            byte[] payload = CreatePayload(checked((int)length));
            Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationFilePath)!);
            await File.WriteAllBytesAsync(request.DestinationFilePath, payload, cancellationToken);
            progress?.Report(new DownloadProgress(length, length, 100));
        }
    }

    private sealed class RecordingProgress : IProgress<PackageUpdateProgress>
    {
        private readonly List<PackageUpdateProgress> _reports = new();

        public IReadOnlyList<PackageUpdateProgress> Reports => _reports;

        public void Report(PackageUpdateProgress value)
        {
            _reports.Add(value);
        }
    }
}
