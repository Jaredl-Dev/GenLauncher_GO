using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Features.Startup;
using GenLauncherGO.Features.Updating;
using GenLauncherGO.Shared.IO;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Features.Updating;

public sealed class S3PackageUpdaterBehaviorTests
{
    [Fact]
    public async Task UpdateAsync_CopiesMatchingFilesFromLatestAndSkipsDownloadAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory), "latest");
        string latestFilePath = Path.Combine(paths.LatestInstalledPath!.FullPath, "Data", "readme.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(latestFilePath)!);
        await File.WriteAllTextAsync(latestFilePath, "payload", TestContext.Current.CancellationToken);

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
        (await File.ReadAllTextAsync(Path.Combine(paths.InstalledPath.FullPath, "Data", "readme.txt"), TestContext.Current.CancellationToken))
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
        await File.WriteAllTextAsync(latestFilePath, latestContents, TestContext.Current.CancellationToken);

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
        (await File.ReadAllBytesAsync(Path.Combine(paths.InstalledPath.FullPath, "Data", "readme.txt"), TestContext.Current.CancellationToken))
            .Should().Equal(CreatePayload(7));
    }

    [Fact]
    public async Task UpdateAsync_ReusesInstalledGibVariantForBigManifestEntryAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory), "latest");
        string latestFilePath = Path.Combine(paths.LatestInstalledPath!.FullPath, "Data", "archive.gib");
        Directory.CreateDirectory(Path.GetDirectoryName(latestFilePath)!);
        await File.WriteAllTextAsync(latestFilePath, "payload", TestContext.Current.CancellationToken);

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
        (await File.ReadAllTextAsync(Path.Combine(paths.InstalledPath.FullPath, "Data", "archive.gib"), TestContext.Current.CancellationToken))
            .Should().Be("payload");
        File.Exists(Path.Combine(paths.InstalledPath.FullPath, "Data", "archive.big")).Should().BeFalse();
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
        await File.WriteAllTextAsync(Path.Combine(paths.TemporaryPath.FullPath, "stale.txt"), "stale", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(paths.TemporaryPath.FullPath, "readme.txt"), "payload", TestContext.Current.CancellationToken);

        S3PackageUpdater updater = CreateUpdater(new RecordingFileDownloader(), new StubFileHashService());

        await updater.UpdateAsync(
            CreateRequest(
                paths,
                new RemoteFileManifestEntry("readme.txt", StubFileHashService.MatchingHash, 7)),
            null,
            CancellationToken.None);

        File.Exists(Path.Combine(paths.InstalledPath.FullPath, "stale.txt")).Should().BeFalse();
        (await File.ReadAllTextAsync(Path.Combine(paths.InstalledPath.FullPath, "readme.txt"), TestContext.Current.CancellationToken))
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
        await File.WriteAllTextAsync(Path.Combine(paths.TemporaryPath.FullPath, "readme.txt"), "payload", TestContext.Current.CancellationToken);
        string outsideFile = Path.Combine(outsidePath, "outside.txt");
        await File.WriteAllTextAsync(outsideFile, "outside", TestContext.Current.CancellationToken);
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
        (await File.ReadAllTextAsync(outsideFile, TestContext.Current.CancellationToken)).Should().Be("outside");
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
        (await File.ReadAllBytesAsync(Path.Combine(paths.TemporaryPath.FullPath, "Data", "readme.txt"), TestContext.Current.CancellationToken))
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
        await File.WriteAllTextAsync(staleFilePath, "stale", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(keepFilePath, "keep", TestContext.Current.CancellationToken);

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
        (await File.ReadAllBytesAsync(staleFilePath, TestContext.Current.CancellationToken)).Should().AllBeEquivalentTo((byte)'x');
        (await File.ReadAllTextAsync(keepFilePath, TestContext.Current.CancellationToken)).Should().Be("keep");
        progress.Reports.Should().ContainSingle(report =>
            report.TotalBytes == 5 &&
            report.BytesRead == 5 &&
            report.ProgressPercentage == 100);
    }

    [Fact]
    public async Task RepairFilesAsync_RejectsLinkedInstalledTreeWithoutMutatingTargetAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        string outsidePath = testDirectory.CreateDirectory("outside");
        string outsideFilePath = Path.Combine(outsidePath, "readme.txt");
        await File.WriteAllTextAsync(outsideFilePath, "outside", TestContext.Current.CancellationToken);
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
        (await File.ReadAllTextAsync(outsideFilePath, TestContext.Current.CancellationToken)).Should().Be("outside");
    }

    [Fact]
    public async Task RepairFilesAsync_ResumesExistingPartialInstalledFileAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        string destinationPath = Path.Combine(paths.InstalledPath.FullPath, "Data", "readme.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await File.WriteAllBytesAsync(destinationPath, [1, 2], TestContext.Current.CancellationToken);
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
        (await File.ReadAllBytesAsync(destinationPath, TestContext.Current.CancellationToken)).Should().Equal([1, 2, 3, 4, 5]);
    }

    [Fact]
    public async Task RepairFilesAsync_ExactSizeHashMismatch_RemovesStaleFileBeforeDownloadAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(TestLauncherPaths.Create(testDirectory));
        string destinationPath = Path.Combine(paths.InstalledPath.FullPath, "Data", "readme.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await File.WriteAllBytesAsync(destinationPath, [1, 2, 3, 4, 5], TestContext.Current.CancellationToken);
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
        (await File.ReadAllBytesAsync(destinationPath, TestContext.Current.CancellationToken)).Should().Equal(CreatePayload(5));
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
        (await File.ReadAllBytesAsync(destinationPath, TestContext.Current.CancellationToken)).Should().Equal([1, 2]);
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

        await repair.Should().ThrowAsync<IOException>();
        downloader.Requests.Should().HaveCount(3);
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
