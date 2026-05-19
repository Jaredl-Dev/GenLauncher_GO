using System;
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
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Services;

public sealed class S3PackageUpdaterBehaviorTests
{
    [Fact]
    public async Task UpdateAsync_CopiesMatchingFilesFromLatestAndSkipsDownloadAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory), "latest");
        string latestFilePath = Path.Combine(paths.LatestInstalledPath!.FullPath, "Data", "readme.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(latestFilePath)!);
        await File.WriteAllTextAsync(latestFilePath, "payload");

        RecordingFileDownloader downloader = new();
        S3PackageUpdater updater = CreateUpdater(downloader, new StubFileHashService());
        RecordingProgress<PackageUpdateProgress> progress = new();

        await updater.UpdateAsync(
            CreateRequest(
                paths,
                new RemoteFileManifestEntry(
                    "Data/readme.txt",
                    StubFileHashService.MatchingHash,
                    (ulong)new FileInfo(latestFilePath).Length)),
            progress,
            CancellationToken.None);

        downloader.Requests.Should().BeEmpty();
        (await File.ReadAllTextAsync(Path.Combine(paths.InstalledPath.FullPath, "Data", "readme.txt")))
            .Should().Be("payload");
        progress.Reports.Should().Contain(report => report.FileName == null);
    }

    [Theory]
    [InlineData("payload", StubFileHashService.MismatchedHash)]
    [InlineData("stale", StubFileHashService.MatchingHash)]
    public async Task UpdateAsync_LatestFileFailingIntegrity_DownloadsReplacementAsync(
        string latestContents,
        string latestHash)
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory), "latest");
        string latestFilePath = Path.Combine(paths.LatestInstalledPath!.FullPath, "Data", "readme.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(latestFilePath)!);
        await File.WriteAllTextAsync(latestFilePath, latestContents);

        RecordingFileDownloader downloader = new();
        S3PackageUpdater updater = CreateUpdater(
            downloader,
            new StubFileHashService
            {
                HashForPath = path => string.Equals(path, latestFilePath, StringComparison.OrdinalIgnoreCase)
                    ? latestHash
                    : StubFileHashService.MatchingHash
            });

        await updater.UpdateAsync(
            CreateRequest(
                paths,
                new RemoteFileManifestEntry("Data/readme.txt", StubFileHashService.MatchingHash, 7)),
            null,
            CancellationToken.None);

        downloader.Requests.Should().ContainSingle();
        (await File.ReadAllBytesAsync(Path.Combine(paths.InstalledPath.FullPath, "Data", "readme.txt")))
            .Should().Equal(CreatePayload(7));
    }

    [Fact]
    public async Task UpdateAsync_ReusesInstalledGibVariantForBigManifestEntryAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory), "latest");
        string latestFilePath = Path.Combine(paths.LatestInstalledPath!.FullPath, "Data", "archive.gib");
        Directory.CreateDirectory(Path.GetDirectoryName(latestFilePath)!);
        await File.WriteAllTextAsync(latestFilePath, "payload");

        RecordingFileDownloader downloader = new();
        S3PackageUpdater updater = CreateUpdater(downloader, new StubFileHashService());

        await updater.UpdateAsync(
            CreateRequest(
                paths,
                new RemoteFileManifestEntry(
                    "Data/archive.big",
                    StubFileHashService.MatchingHash,
                    (ulong)new FileInfo(latestFilePath).Length)),
            null,
            CancellationToken.None);

        downloader.Requests.Should().BeEmpty();
        (await File.ReadAllTextAsync(Path.Combine(paths.InstalledPath.FullPath, "Data", "archive.gib")))
            .Should().Be("payload");
        File.Exists(Path.Combine(paths.InstalledPath.FullPath, "Data", "archive.big")).Should().BeFalse();
    }

    /// <summary>
    ///     Files reused from an earlier version still count towards the package's size and progress, so the reported
    ///     total is what the user is actually installing rather than only the part that crossed the network.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReportsWholePackageBytesWhenLatestFilesAreReusedAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory), "latest");
        string latestFilePath = Path.Combine(paths.LatestInstalledPath!.FullPath, "Data", "reused.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(latestFilePath)!);
        await File.WriteAllBytesAsync(latestFilePath, CreatePayload(660));

        RecordingFileDownloader downloader = new();
        S3PackageUpdater updater = CreateUpdater(downloader, new StubFileHashService());
        RecordingProgress<PackageUpdateProgress> progress = new();

        await updater.UpdateAsync(
            CreateRequest(
                paths,
                new RemoteFileManifestEntry("Data/reused.txt", StubFileHashService.MatchingHash, 660),
                new RemoteFileManifestEntry("Data/missing.txt", StubFileHashService.MatchingHash, 20)),
            progress,
            CancellationToken.None);

        downloader.Requests.Should().ContainSingle();
        progress.Reports.Should().ContainSingle();
        progress.Reports[0].TotalBytes.Should().Be(680);
        progress.Reports[0].BytesRead.Should().Be(680);
        progress.Reports[0].ProgressPercentage.Should().Be(100);
    }

    /// <summary>
    ///     A resumed transfer reports against the whole package: the 5 bytes already staged count as progress rather
    ///     than shrinking the total, so the bar continues from where it stopped instead of restarting at zero.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReportsWholePackageBytesForPartialStagedDownloadAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        string partialFilePath = Path.Combine(paths.TemporaryPath.FullPath, "Data", "missing.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(partialFilePath)!);
        await File.WriteAllBytesAsync(partialFilePath, CreatePayload(5));

        RecordingFileDownloader downloader = new();
        S3PackageUpdater updater = CreateUpdater(downloader, new StubFileHashService());
        RecordingProgress<PackageUpdateProgress> progress = new();

        await updater.UpdateAsync(
            CreateRequest(
                paths,
                new RemoteFileManifestEntry("Data/missing.txt", StubFileHashService.MatchingHash, 20)),
            progress,
            CancellationToken.None);

        downloader.Requests.Should().ContainSingle();
        progress.Reports.Should().ContainSingle();
        progress.Reports[0].TotalBytes.Should().Be(20);
        progress.Reports[0].BytesRead.Should().Be(20);
        progress.Reports[0].ProgressPercentage.Should().Be(100);
    }

    [Fact]
    public async Task UpdateAsync_RejectsManifestPathOutsideTemporaryFolderAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths launcherPaths = TestLauncherPaths.Create(testDirectory);
        PackageUpdatePathSet paths = CreatePackagePaths(launcherPaths);
        S3PackageUpdater updater = CreateUpdater(new RecordingFileDownloader(), new StubFileHashService());

        Func<Task> act = async () => await updater.UpdateAsync(
            CreateRequest(
                paths,
                new RemoteFileManifestEntry("../escape.txt", StubFileHashService.MatchingHash, 1)),
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        File.Exists(Path.Combine(launcherPaths.PackagesDirectory, "escape.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_PrunesStaleTemporaryFilesBeforeInstallingAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths launcherPaths = TestLauncherPaths.Create(testDirectory);
        PackageUpdatePathSet paths = TestPackageUpdatePaths.Create(
            launcherPaths,
            Path.Combine("NProject Mod", "2.11"),
            "installed");
        Directory.CreateDirectory(paths.TemporaryPath.FullPath);
        await File.WriteAllTextAsync(Path.Combine(paths.TemporaryPath.FullPath, "stale.txt"), "stale");
        await File.WriteAllTextAsync(Path.Combine(paths.TemporaryPath.FullPath, "readme.txt"), "payload");

        S3PackageUpdater updater = CreateUpdater(new RecordingFileDownloader(), new StubFileHashService());

        await updater.UpdateAsync(
            CreateRequest(
                paths,
                new RemoteFileManifestEntry("readme.txt", StubFileHashService.MatchingHash, 7)),
            null,
            CancellationToken.None);

        File.Exists(Path.Combine(paths.InstalledPath.FullPath, "stale.txt")).Should().BeFalse();
        (await File.ReadAllTextAsync(Path.Combine(paths.InstalledPath.FullPath, "readme.txt")))
            .Should().Be("payload");
        Directory.Exists(launcherPaths.PackagesDirectory).Should().BeFalse();
        Directory.Exists(launcherPaths.TempDirectory).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_RemovesUnsafeStagingLinkWithoutDeletingTargetAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        string outsidePath = testDirectory.CreateDirectory("outside");
        Directory.CreateDirectory(paths.TemporaryPath.FullPath);
        await File.WriteAllTextAsync(Path.Combine(paths.TemporaryPath.FullPath, "readme.txt"), "payload");
        string outsideFile = Path.Combine(outsidePath, "outside.txt");
        await File.WriteAllTextAsync(outsideFile, "outside");
        ReparsePointTestSupport.CreateDirectoryJunction(
            Path.Combine(paths.TemporaryPath.FullPath, "linked"),
            outsidePath);

        S3PackageUpdater updater = CreateUpdater(new RecordingFileDownloader(), new StubFileHashService());

        await updater.UpdateAsync(
            CreateRequest(
                paths,
                new RemoteFileManifestEntry("readme.txt", StubFileHashService.MatchingHash, 7)),
            null,
            CancellationToken.None);

        Directory.Exists(Path.Combine(paths.InstalledPath.FullPath, "linked")).Should().BeFalse();
        (await File.ReadAllTextAsync(outsideFile)).Should().Be("outside");
    }

    /// <summary>
    ///     Cancellation between the transfer and the install must leave the installed folder untouched and keep the
    ///     staged bytes, so the next attempt resumes instead of fetching the package again.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_CancellationAfterDownload_KeepsStagedFileAndSkipsInstallAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        using CancellationTokenSource cancellation = new();
        RecordingFileDownloader downloader = new()
        {
            Handler = async (request, _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationFilePath)!);
                await File.WriteAllBytesAsync(
                    request.DestinationFilePath,
                    CreatePayload(5),
                    CancellationToken.None);
                await cancellation.CancelAsync();
            }
        };
        S3PackageUpdater updater = CreateUpdater(downloader, new StubFileHashService());

        Func<Task> update = () => updater.UpdateAsync(
            CreateRequest(
                paths,
                new RemoteFileManifestEntry("Data/readme.txt", StubFileHashService.MatchingHash, 5)),
            null,
            cancellation.Token);

        await update.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(paths.InstalledPath.FullPath).Should().BeFalse();
        (await File.ReadAllBytesAsync(Path.Combine(paths.TemporaryPath.FullPath, "Data", "readme.txt")))
            .Should().Equal(CreatePayload(5));
    }

    [Theory]
    [InlineData("Data/readme.txt")]
    [InlineData(@"Data\readme.txt")]
    public async Task RepairFilesAsync_DownloadsSelectedModifiedFileInPlaceAsync(string manifestFileName)
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        string staleFilePath = Path.Combine(paths.InstalledPath.FullPath, "Data", "readme.txt");
        string keepFilePath = Path.Combine(paths.InstalledPath.FullPath, "Data", "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(staleFilePath)!);
        await File.WriteAllTextAsync(staleFilePath, "stale");
        await File.WriteAllTextAsync(keepFilePath, "keep");

        RecordingFileDownloader downloader = new();
        StubFileHashService hashService = new()
        {
            HashForPath = path => File.ReadAllBytes(path).All(value => value == (byte)'x')
                ? StubFileHashService.MatchingHash
                : StubFileHashService.MismatchedHash
        };
        S3PackageUpdater updater = CreateUpdater(downloader, hashService);
        RecordingProgress<PackageUpdateProgress> progress = new();

        await updater.RepairFilesAsync(
            CreateRepairRequest(
                paths.InstalledPath,
                new RemoteFileManifestEntry(manifestFileName, StubFileHashService.MatchingHash, 5)),
            progress,
            CancellationToken.None);

        DownloadFileRequest request = downloader.Requests.Should().ContainSingle().Which;
        request.DestinationFilePath.Should().Be(staleFilePath);
        request.SourceUri.AbsolutePath.Should().Be("/mods/folder/Data/readme.txt");
        (await File.ReadAllBytesAsync(staleFilePath)).Should().AllBeEquivalentTo((byte)'x');
        (await File.ReadAllTextAsync(keepFilePath)).Should().Be("keep");
        progress.Reports.Should().ContainSingle(report =>
            report.TotalBytes == 5 &&
            report.BytesRead == 5 &&
            report.ProgressPercentage == 100);
    }

    /// <summary>
    ///     S3 publishes ETags while the launcher computes MD5 sums, and the two differ only in letter case; treating
    ///     that as corruption would re-download a file that is already correct.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RepairFilesAsync_HashLetterCaseDiffers_SkipsDownloadAsync(bool manifestHashIsLowercase)
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        string installedFilePath = Path.Combine(paths.InstalledPath.FullPath, "Data", "readme.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(installedFilePath)!);
        await File.WriteAllBytesAsync(installedFilePath, CreatePayload(5));
        string lowercaseHash = StubFileHashService.MatchingHash.ToLowerInvariant();
        string manifestHash = manifestHashIsLowercase ? lowercaseHash : StubFileHashService.MatchingHash;
        string computedHash = manifestHashIsLowercase ? StubFileHashService.MatchingHash : lowercaseHash;

        RecordingFileDownloader downloader = new();
        S3PackageUpdater updater = CreateUpdater(
            downloader,
            new StubFileHashService { HashForPath = _ => computedHash });

        await updater.RepairFilesAsync(
            CreateRepairRequest(
                paths.InstalledPath,
                new RemoteFileManifestEntry("Data/readme.txt", manifestHash, 5)),
            null,
            CancellationToken.None);

        downloader.Requests.Should().BeEmpty();
        (await File.ReadAllBytesAsync(installedFilePath)).Should().Equal(CreatePayload(5));
    }

    [Fact]
    public async Task RepairFilesAsync_RejectsLinkedInstalledTreeWithoutMutatingTargetAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        string outsidePath = testDirectory.CreateDirectory("outside");
        string outsideFilePath = Path.Combine(outsidePath, "readme.txt");
        await File.WriteAllTextAsync(outsideFilePath, "outside");
        ReparsePointTestSupport.CreateDirectoryJunction(paths.InstalledPath.FullPath, outsidePath);
        RecordingFileDownloader downloader = new();
        S3PackageUpdater updater = CreateUpdater(downloader, new StubFileHashService());

        Func<Task> repair = () => updater.RepairFilesAsync(
            CreateRepairRequest(
                paths.InstalledPath,
                new RemoteFileManifestEntry("readme.txt", StubFileHashService.MatchingHash, 5)),
            null,
            CancellationToken.None);

        await repair.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*reparse point*");
        downloader.Requests.Should().BeEmpty();
        (await File.ReadAllTextAsync(outsideFilePath)).Should().Be("outside");
    }

    [Fact]
    public async Task RepairFilesAsync_DownloadFailureRetainsPartialFileForResumeAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        string destinationPath = Path.Combine(paths.InstalledPath.FullPath, "Data", "readme.txt");
        RecordingFileDownloader downloader = new()
        {
            Handler = async (request, cancellationToken) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationFilePath)!);
                await File.WriteAllBytesAsync(request.DestinationFilePath, [1, 2], cancellationToken);
                throw new IOException("connection interrupted");
            }
        };
        S3PackageUpdater updater = CreateUpdater(downloader, new StubFileHashService());

        Func<Task> repair = () => updater.RepairFilesAsync(
            CreateRepairRequest(
                paths.InstalledPath,
                new RemoteFileManifestEntry("Data/readme.txt", StubFileHashService.MatchingHash, 5)),
            null,
            CancellationToken.None);

        await repair.Should().ThrowAsync<IOException>()
            .WithMessage("connection interrupted");
        DownloadFileRequest request = downloader.Requests.Should().ContainSingle().Which;
        request.Resume.Should().BeTrue();
        (await File.ReadAllBytesAsync(destinationPath)).Should().Equal([1, 2]);
    }

    [Fact]
    public async Task RepairFilesAsync_ResumesExistingPartialInstalledFileAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        string destinationPath = Path.Combine(paths.InstalledPath.FullPath, "Data", "readme.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await File.WriteAllBytesAsync(destinationPath, [1, 2]);
        RecordingFileDownloader downloader = new()
        {
            Handler = async (request, cancellationToken) =>
            {
                request.Resume.Should().BeTrue();
                (await File.ReadAllBytesAsync(request.DestinationFilePath, cancellationToken))
                    .Should().Equal([1, 2]);
                await using FileStream stream = new(
                    request.DestinationFilePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    4096,
                    FileOptions.Asynchronous);
                await stream.WriteAsync(new byte[] { 3, 4, 5 }, cancellationToken);
            }
        };
        S3PackageUpdater updater = CreateUpdater(downloader, new StubFileHashService());

        await updater.RepairFilesAsync(
            CreateRepairRequest(
                paths.InstalledPath,
                new RemoteFileManifestEntry("Data/readme.txt", StubFileHashService.MatchingHash, 5)),
            null,
            CancellationToken.None);

        downloader.Requests.Should().ContainSingle();
        (await File.ReadAllBytesAsync(destinationPath)).Should().Equal([1, 2, 3, 4, 5]);
    }

    [Fact]
    public async Task RepairFilesAsync_ExactSizeHashMismatch_RemovesStaleFileBeforeDownloadAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        string destinationPath = Path.Combine(paths.InstalledPath.FullPath, "Data", "readme.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await File.WriteAllBytesAsync(destinationPath, [1, 2, 3, 4, 5]);
        bool staleFilePresentWhenDownloadStarted = false;
        RecordingFileDownloader downloader = new()
        {
            Handler = async (request, cancellationToken) =>
            {
                staleFilePresentWhenDownloadStarted = File.Exists(request.DestinationFilePath);
                await File.WriteAllBytesAsync(
                    request.DestinationFilePath,
                    CreatePayload(5),
                    cancellationToken);
            }
        };
        S3PackageUpdater updater = CreateUpdater(
            downloader,
            new StubFileHashService
            {
                HashForPath = path => File.ReadAllBytes(path).All(value => value == (byte)'x')
                    ? StubFileHashService.MatchingHash
                    : StubFileHashService.MismatchedHash
            });

        await updater.RepairFilesAsync(
            CreateRepairRequest(
                paths.InstalledPath,
                new RemoteFileManifestEntry("Data/readme.txt", StubFileHashService.MatchingHash, 5)),
            null,
            CancellationToken.None);

        staleFilePresentWhenDownloadStarted.Should().BeFalse();
        downloader.Requests.Should().ContainSingle();
        (await File.ReadAllBytesAsync(destinationPath)).Should().Equal(CreatePayload(5));
    }

    [Theory]
    [InlineData(true, 20, 19)]
    [InlineData(false, 35, 34)]
    public async Task RepairFilesAsync_ResumeProgress_DistinguishesAcceptedRangeFromRestartAsync(
        bool serverAcceptedResume,
        long expectedTotalBytes,
        long expectedIntermediateBytes)
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        string destinationPath = Path.Combine(paths.InstalledPath.FullPath, "Data", "readme.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await File.WriteAllBytesAsync(destinationPath, CreatePayload(15));
        RecordingFileDownloader downloader = new()
        {
            ProgressHandler = async (request, progress, cancellationToken) =>
            {
                progress?.Report(new DownloadProgress(
                    20,
                    serverAcceptedResume ? 15 : 0,
                    serverAcceptedResume ? 75 : 0));
                if (serverAcceptedResume)
                {
                    await using FileStream stream = new(
                        request.DestinationFilePath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.Read,
                        4096,
                        FileOptions.Asynchronous);
                    await stream.WriteAsync(CreatePayload(5), cancellationToken);
                }
                else
                {
                    await File.WriteAllBytesAsync(
                        request.DestinationFilePath,
                        CreatePayload(20),
                        cancellationToken);
                }

                progress?.Report(new DownloadProgress(20, 20, 100));
            }
        };
        S3PackageUpdater updater = CreateUpdater(downloader, new StubFileHashService());
        RecordingProgress<PackageUpdateProgress> progress = new();

        await updater.RepairFilesAsync(
            CreateRepairRequest(
                paths.InstalledPath,
                new RemoteFileManifestEntry("Data/readme.txt", StubFileHashService.MatchingHash, 20)),
            progress,
            CancellationToken.None);

        progress.Reports.Select(report => report.TotalBytes)
            .Should().OnlyContain(totalBytes => totalBytes == expectedTotalBytes);
        progress.Reports.Select(report => report.BytesRead)
            .Should().Equal(15, expectedIntermediateBytes, expectedTotalBytes);
        progress.Reports.Take(progress.Reports.Count - 1)
            .Should().OnlyContain(report => report.ProgressPercentage < 100);
        progress.Reports[^1].ProgressPercentage.Should().Be(100);
    }

    [Fact]
    public async Task RepairFilesAsync_SizeOnlyFileMatches_SkipsDownloadAndHashAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        string destinationPath = Path.Combine(paths.InstalledPath.FullPath, "Data", "asset.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await File.WriteAllBytesAsync(destinationPath, [1, 2, 3, 4, 5]);
        RecordingFileDownloader downloader = new();
        S3PackageUpdater updater = CreateUpdater(
            downloader,
            new StubFileHashService
            {
                HashForPath = _ => throw new InvalidOperationException(
                    "Size-only package files must not be hashed.")
            });

        await updater.RepairFilesAsync(
            CreateRepairRequest(
                paths.InstalledPath,
                new RemoteFileManifestEntry("Data/asset.bin", string.Empty, 5)),
            null,
            CancellationToken.None);

        downloader.Requests.Should().BeEmpty();
        (await File.ReadAllBytesAsync(destinationPath)).Should().Equal([1, 2, 3, 4, 5]);
    }

    [Fact]
    public async Task RepairFilesAsync_MoreThanSixFiles_CompletesEveryDownloadAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        RemoteFileManifestEntry[] files = Enumerable.Range(0, 7)
            .Select(index => new RemoteFileManifestEntry($"Data/file-{index}.bin", string.Empty, 1))
            .ToArray();
        RecordingFileDownloader downloader = new();
        S3PackageUpdater updater = CreateUpdater(downloader, new StubFileHashService());
        using CancellationTokenSource cancellation = new(TestTimeouts.Wait);

        await updater.RepairFilesAsync(
            CreateRepairRequest(paths.InstalledPath, files),
            null,
            cancellation.Token);

        downloader.Requests.Should().HaveCount(files.Length);
        foreach (RemoteFileManifestEntry file in files)
        {
            File.Exists(Path.Combine(paths.InstalledPath.FullPath, file.FileName)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task RepairFilesAsync_CancellationRetainsPartialFileForResumeAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        string destinationPath = Path.Combine(paths.InstalledPath.FullPath, "Data", "readme.txt");
        using CancellationTokenSource cancellation = new();
        RecordingFileDownloader downloader = new()
        {
            Handler = async (request, cancellationToken) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationFilePath)!);
                await File.WriteAllBytesAsync(request.DestinationFilePath, [1, 2], CancellationToken.None);
                await cancellation.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }
        };
        S3PackageUpdater updater = CreateUpdater(downloader, new StubFileHashService());

        Func<Task> repair = () => updater.RepairFilesAsync(
            CreateRepairRequest(
                paths.InstalledPath,
                new RemoteFileManifestEntry("Data/readme.txt", StubFileHashService.MatchingHash, 5)),
            null,
            cancellation.Token);

        await repair.Should().ThrowAsync<OperationCanceledException>();
        downloader.Requests.Should().ContainSingle().Which.Resume.Should().BeTrue();
        (await File.ReadAllBytesAsync(destinationPath)).Should().Equal([1, 2]);
    }

    [Fact]
    public async Task RepairFilesAsync_RepeatedHashMismatchFailsAfterThreeAttemptsAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        RecordingFileDownloader downloader = new();
        S3PackageUpdater updater = CreateUpdater(
            downloader,
            new StubFileHashService { HashForPath = _ => StubFileHashService.MismatchedHash });

        Func<Task> repair = () => updater.RepairFilesAsync(
            CreateRepairRequest(
                paths.InstalledPath,
                new RemoteFileManifestEntry("Data/readme.txt", StubFileHashService.MatchingHash, 5)),
            null,
            CancellationToken.None);

        await repair.Should().ThrowAsync<IOException>()
            .WithMessage("*Hash sum mismatch*");
        downloader.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task RepairFilesAsync_BigFileHashRetry_RemovesConvertedFailureBeforeRetryAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        string bigPath = Path.Combine(paths.InstalledPath.FullPath, "Data", "archive.big");
        string gibPath = Path.Combine(paths.InstalledPath.FullPath, "Data", "archive.gib");
        Directory.CreateDirectory(Path.GetDirectoryName(bigPath)!);
        await File.WriteAllBytesAsync(bigPath, CreatePayload(2));
        int downloadAttempt = 0;
        bool failedVariantsRemovedBeforeRetry = false;
        RecordingFileDownloader downloader = new()
        {
            ProgressHandler = async (request, progress, cancellationToken) =>
            {
                downloadAttempt++;
                if (downloadAttempt == 1)
                {
                    progress?.Report(new DownloadProgress(5, 2, 40));
                    await using FileStream stream = new(
                        request.DestinationFilePath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.Read,
                        4096,
                        FileOptions.Asynchronous);
                    await stream.WriteAsync(CreatePayload(3), cancellationToken);
                }
                else
                {
                    failedVariantsRemovedBeforeRetry =
                        !File.Exists(bigPath) && !File.Exists(gibPath);
                    progress?.Report(new DownloadProgress(5, 0, 0));
                    Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationFilePath)!);
                    await File.WriteAllBytesAsync(
                        request.DestinationFilePath,
                        CreatePayload(5),
                        cancellationToken);
                }

                progress?.Report(new DownloadProgress(5, 5, 100));
            }
        };
        int hashAttempt = 0;
        S3PackageUpdater updater = CreateUpdater(
            downloader,
            new StubFileHashService
            {
                HashForPath = _ => Interlocked.Increment(ref hashAttempt) == 1
                    ? StubFileHashService.MismatchedHash
                    : StubFileHashService.MatchingHash
            });
        RecordingProgress<PackageUpdateProgress> progress = new();

        await updater.RepairFilesAsync(
            CreateRepairRequest(
                paths.InstalledPath,
                new RemoteFileManifestEntry("Data/archive.big", StubFileHashService.MatchingHash, 5)),
            progress,
            CancellationToken.None);

        downloader.Requests.Should().HaveCount(2);
        failedVariantsRemovedBeforeRetry.Should().BeTrue();
        progress.Reports[^1].TotalBytes.Should().Be(10);
        progress.Reports[^1].BytesRead.Should().Be(10);
        progress.Reports[^1].ProgressPercentage.Should().Be(100);
        File.Exists(bigPath).Should().BeFalse();
        (await File.ReadAllBytesAsync(gibPath)).Should().Equal(CreatePayload(5));
    }

    [Fact]
    public async Task UpdateAsync_HashRetry_ReportsMonotonicProgressAndOnlyFinishesAtOneHundredAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        int hashAttempt = 0;
        RecordingFileDownloader downloader = new()
        {
            ProgressHandler = async (request, progress, cancellationToken) =>
            {
                long length = request.ExpectedBytes.GetValueOrDefault();
                Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationFilePath)!);
                await File.WriteAllBytesAsync(
                    request.DestinationFilePath,
                    CreatePayload(checked((int)length)),
                    cancellationToken);
                progress?.Report(new DownloadProgress(length, length, 100));
            }
        };
        S3PackageUpdater updater = CreateUpdater(
            downloader,
            new StubFileHashService
            {
                HashForPath = _ => Interlocked.Increment(ref hashAttempt) == 1
                    ? StubFileHashService.MismatchedHash
                    : StubFileHashService.MatchingHash
            });
        RecordingProgress<PackageUpdateProgress> progress = new();

        await updater.UpdateAsync(
            CreateRequest(
                paths,
                new RemoteFileManifestEntry("Data/file.txt", StubFileHashService.MatchingHash, 5)),
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
    public async Task UpdateAsync_RejectsDuplicateNormalizedManifestDestinationsAsync(
        string firstFile,
        string secondFile)
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        S3PackageUpdater updater = CreateUpdater(new RecordingFileDownloader(), new StubFileHashService());
        S3PackageUpdateRequest request = CreateRequest(
            paths,
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

    private static PackageUpdatePathSet CreatePackagePaths(
        LauncherPaths launcherPaths,
        string? latestRelativePath = null)
    {
        return TestPackageUpdatePaths.Create(launcherPaths, "temp", "installed", latestRelativePath);
    }

    private static S3PackageUpdateRequest CreateRequest(
        PackageUpdatePathSet paths,
        params RemoteFileManifestEntry[] files)
    {
        return new S3PackageUpdateRequest(
            files,
            CreateSource(),
            paths,
            CreateHashCheckedExtensions());
    }

    private static S3PackageFileRepairRequest CreateRepairRequest(
        OwnedContentPath installedPath,
        params RemoteFileManifestEntry[] files)
    {
        return new S3PackageFileRepairRequest(
            files,
            CreateSource(),
            installedPath,
            CreateHashCheckedExtensions());
    }

    private static HashSet<string> CreateHashCheckedExtensions()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt", ".big", ".gib" };
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
}
