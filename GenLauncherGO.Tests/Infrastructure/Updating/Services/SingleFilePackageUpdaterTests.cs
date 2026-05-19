using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Services;

public sealed class SingleFilePackageUpdaterTests
{
    private static readonly string _versionRelativePath = Path.Combine("NProject Mod", "2.11");

    [Fact]
    public async Task UpdateAsync_ClearsStaleTemporaryFilesBeforeInstallingAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        Directory.CreateDirectory(paths.TemporaryPath.FullPath);
        await File.WriteAllTextAsync(Path.Combine(paths.TemporaryPath.FullPath, "stale.txt"), "stale");
        SingleFilePackageUpdater updater = CreateUpdater(
            CreateWritingDownloader("payload"),
            new StubDownloadFileMetadataReader("readme.txt", 7),
            CreateUnusedArchiveExtractor());

        await updater.UpdateAsync(
            new Uri("https://example.test/readme.txt"),
            paths,
            null,
            CancellationToken.None);

        File.Exists(Path.Combine(paths.InstalledPath.FullPath, "stale.txt")).Should().BeFalse();
        (await File.ReadAllTextAsync(Path.Combine(paths.InstalledPath.FullPath, "readme.txt")))
            .Should().Be("payload");
    }

    [Fact]
    public async Task UpdateAsync_RemovesEmptyPackageStagingParentsAfterInstallingAsync()
    {
        using TestDirectory testDirectory = new();
        LauncherPaths launcherPaths = TestLauncherPaths.Create(testDirectory);
        PackageUpdatePathSet paths = TestPackageUpdatePaths.Create(
            launcherPaths,
            _versionRelativePath,
            _versionRelativePath);
        SingleFilePackageUpdater updater = CreateUpdater(
            CreateWritingDownloader("payload"),
            new StubDownloadFileMetadataReader("readme.txt", 7),
            CreateUnusedArchiveExtractor());

        await updater.UpdateAsync(
            new Uri("https://example.test/readme.txt"),
            paths,
            null,
            CancellationToken.None);

        (await File.ReadAllTextAsync(Path.Combine(paths.InstalledPath.FullPath, "readme.txt")))
            .Should().Be("payload");
        Directory.Exists(launcherPaths.PackagesDirectory).Should().BeFalse();
        Directory.Exists(launcherPaths.TempDirectory).Should().BeTrue();
    }

    [Theory]
    [InlineData(".zip")]
    [InlineData(".rar")]
    [InlineData(".7z")]
    public async Task UpdateAsync_ExtractsArchiveDeletesDownloadedArchiveAndInstallsExtractedFilesAsync(
        string extension)
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        string archiveFileName = "package" + extension;
        RecordingArchiveExtractor archiveExtractor = new()
        {
            ExtractHandler = destinationDirectory => File.WriteAllText(
                Path.Combine(destinationDirectory, "extracted.gib"),
                "extracted")
        };
        SingleFilePackageUpdater updater = CreateUpdater(
            CreateWritingDownloader("archive"),
            new StubDownloadFileMetadataReader(archiveFileName, 7),
            archiveExtractor);

        await updater.UpdateAsync(
            new Uri("https://example.test/" + archiveFileName),
            paths,
            null,
            CancellationToken.None);

        File.Exists(Path.Combine(paths.InstalledPath.FullPath, archiveFileName)).Should().BeFalse();
        (await File.ReadAllTextAsync(Path.Combine(paths.InstalledPath.FullPath, "extracted.gib")))
            .Should().Be("extracted");
        Path.GetFileName(archiveExtractor.ArchiveFilePath!).Should().Be(archiveFileName);
        archiveExtractor.ConvertBigFilesToGib.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../escape.zip")]
    [InlineData(@"nested\escape.zip")]
    [InlineData("nested/escape.zip")]
    [InlineData("C:escape.zip")]
    [InlineData("<escape.zip")]
    public async Task UpdateAsync_RejectsUnsafeRemoteMetadataFileNameAsync(string fileName)
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        SingleFilePackageUpdater updater = CreateUpdater(
            CreateWritingDownloader("payload"),
            new StubDownloadFileMetadataReader(fileName, 7),
            CreateUnusedArchiveExtractor());

        Func<Task> update = () => updater.UpdateAsync(
            new Uri("https://example.test/package.zip"),
            paths,
            null,
            CancellationToken.None);

        await update.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*safe direct file name*");
        Directory.Exists(paths.InstalledPath.FullPath).Should().BeFalse();
        File.Exists(Path.Combine(testDirectory.Path, "escape.zip")).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_RejectsLinkedInstalledRootWithoutDeletingTargetAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        string outsidePath = testDirectory.CreateDirectory("outside");
        string outsideFile = Path.Combine(outsidePath, "outside.txt");
        await File.WriteAllTextAsync(outsideFile, "outside");
        Directory.CreateDirectory(Path.GetDirectoryName(paths.InstalledPath.FullPath)!);
        ReparsePointTestSupport.CreateDirectoryJunction(paths.InstalledPath.FullPath, outsidePath);
        SingleFilePackageUpdater updater = CreateUpdater(
            CreateWritingDownloader("payload"),
            new StubDownloadFileMetadataReader("readme.txt", 7),
            CreateUnusedArchiveExtractor());

        Func<Task> update = () => updater.UpdateAsync(
            new Uri("https://example.test/readme.txt"),
            paths,
            null,
            CancellationToken.None);

        await update.Should().ThrowAsync<IOException>();
        (await File.ReadAllTextAsync(outsideFile)).Should().Be("outside");
    }

    /// <summary>
    ///     Cancellation between the transfer and the install must not leave a half-installed package: nothing is moved
    ///     into place, and the staged bytes stay for the resumed attempt.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_CancellationAfterDownload_KeepsStagedFileAndSkipsInstallAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        using CancellationTokenSource cancellation = new();
        RecordingFileDownloader downloader = new()
        {
            Handler = async (request, _) =>
            {
                await File.WriteAllTextAsync(request.DestinationFilePath, "payload", CancellationToken.None);
                await cancellation.CancelAsync();
            }
        };
        SingleFilePackageUpdater updater = CreateUpdater(
            downloader,
            new StubDownloadFileMetadataReader("readme.txt", 7),
            CreateUnusedArchiveExtractor());

        Func<Task> update = () => updater.UpdateAsync(
            new Uri("https://example.test/readme.txt"),
            paths,
            null,
            cancellation.Token);

        await update.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(paths.InstalledPath.FullPath).Should().BeFalse();
        (await File.ReadAllTextAsync(Path.Combine(paths.TemporaryPath.FullPath, "readme.txt")))
            .Should().Be("payload");
    }

    /// <summary>
    ///     Package progress is reported against the remote file's declared size while the transfer runs, and the request
    ///     asks to resume so an interrupted attempt continues instead of refetching bytes already on disk.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReportsTransferProgressAgainstDeclaredPackageSizeAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        RecordingFileDownloader downloader = new()
        {
            ProgressHandler = async (request, progress, cancellationToken) =>
            {
                progress?.Report(new DownloadProgress(7, 3, 42.86));
                await File.WriteAllTextAsync(request.DestinationFilePath, "payload", cancellationToken);
                progress?.Report(new DownloadProgress(7, 7, 100));
            }
        };
        SingleFilePackageUpdater updater = CreateUpdater(
            downloader,
            new StubDownloadFileMetadataReader("readme.txt", 7),
            CreateUnusedArchiveExtractor());
        RecordingProgress<PackageUpdateProgress> progress = new();

        await updater.UpdateAsync(
            new Uri("https://example.test/readme.txt"),
            paths,
            progress,
            CancellationToken.None);

        progress.Reports[0].TotalBytes.Should().Be(7);
        progress.Reports[0].BytesRead.Should().Be(3);
        progress.Reports[^1].TotalBytes.Should().Be(7);
        progress.Reports[^1].BytesRead.Should().Be(7);
        progress.Reports[^1].ProgressPercentage.Should().Be(100);
        DownloadFileRequest request = downloader.Requests.Should().ContainSingle().Which;
        request.Resume.Should().BeTrue();
        request.ExpectedBytes.Should().Be(7);
    }

    /// <summary>
    ///     A paused download must not clear the staging folder or start a transfer: the user paused to keep what is
    ///     already on disk, and resuming has to continue that attempt rather than begin a fresh one.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_PausedBeforeStart_DoesNoStagingWorkUntilResumedAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        Directory.CreateDirectory(paths.TemporaryPath.FullPath);
        string stagedFilePath = Path.Combine(paths.TemporaryPath.FullPath, "stale.txt");
        await File.WriteAllTextAsync(stagedFilePath, "stale");
        SingleFilePackageUpdater updater = CreateUpdater(
            CreateWritingDownloader("payload"),
            new StubDownloadFileMetadataReader("readme.txt", 7),
            CreateUnusedArchiveExtractor());
        PackageDownloadPauseController pauseController = new();
        pauseController.Pause();

        Task update = updater.UpdateAsync(
            new Uri("https://example.test/readme.txt"),
            paths,
            null,
            CancellationToken.None,
            pauseController);
        bool stagingUntouchedWhilePaused = File.Exists(stagedFilePath);
        pauseController.Resume();
        await update;

        stagingUntouchedWhilePaused.Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(paths.InstalledPath.FullPath, "readme.txt")))
            .Should().Be("payload");
    }

    /// <summary>
    ///     Cancellation between extraction and installation keeps the downloaded archive, so the retry resumes from the
    ///     bytes already transferred instead of fetching the whole package again.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_CanceledDuringExtraction_KeepsTheDownloadedArchiveAndSkipsInstallAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        using CancellationTokenSource cancellation = new();
        RecordingArchiveExtractor archiveExtractor = new()
        {
            ExtractHandler = destinationDirectory =>
            {
                File.WriteAllText(Path.Combine(destinationDirectory, "extracted.gib"), "extracted");
                cancellation.Cancel();
            }
        };
        SingleFilePackageUpdater updater = CreateUpdater(
            CreateWritingDownloader("archive"),
            new StubDownloadFileMetadataReader("package.zip", 7),
            archiveExtractor);

        Func<Task> update = () => updater.UpdateAsync(
            new Uri("https://example.test/package.zip"),
            paths,
            null,
            cancellation.Token);

        await update.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(paths.InstalledPath.FullPath).Should().BeFalse();
        File.Exists(Path.Combine(paths.TemporaryPath.FullPath, "package.zip")).Should().BeTrue();
    }

    /// <summary>
    ///     A remote that declares no package size still has to publish the transfer's closing byte count, or the panel
    ///     is left showing a mid-transfer figure after the download has already finished.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_UndeclaredPackageSize_ReportsTheFinalTransferredByteCountAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        RecordingFileDownloader downloader = new()
        {
            ProgressHandler = async (request, progress, cancellationToken) =>
            {
                await File.WriteAllTextAsync(request.DestinationFilePath, "payload", cancellationToken);
                progress?.Report(new DownloadProgress(7, 3, null));
                progress?.Report(new DownloadProgress(7, 7, 100));
            }
        };
        SingleFilePackageUpdater updater = CreateUpdater(
            downloader,
            new StubDownloadFileMetadataReader("readme.txt", null),
            CreateUnusedArchiveExtractor());
        RecordingProgress<PackageUpdateProgress> progress = new();

        await updater.UpdateAsync(
            new Uri("https://example.test/readme.txt"),
            paths,
            progress,
            CancellationToken.None);

        progress.Reports[^1].BytesRead.Should().Be(7);
    }

    /// <summary>
    ///     A transfer whose own end the downloader cannot state still moves the byte counter; only the closing report
    ///     depends on knowing where the transfer ends.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_TransferSizeUnknown_StillReportsTransferredBytesAsync()
    {
        using TestDirectory testDirectory = new();
        PackageUpdatePathSet paths = CreatePackagePaths(testDirectory);
        RecordingFileDownloader downloader = new()
        {
            ProgressHandler = async (request, progress, cancellationToken) =>
            {
                progress?.Report(new DownloadProgress(null, 3, null));
                await File.WriteAllTextAsync(request.DestinationFilePath, "payload", cancellationToken);
            }
        };
        SingleFilePackageUpdater updater = CreateUpdater(
            downloader,
            new StubDownloadFileMetadataReader("readme.txt", 7),
            CreateUnusedArchiveExtractor());
        RecordingProgress<PackageUpdateProgress> progress = new();

        await updater.UpdateAsync(
            new Uri("https://example.test/readme.txt"),
            paths,
            progress,
            CancellationToken.None);

        progress.Reports.Should().ContainSingle().Which.BytesRead.Should().Be(3);
    }

    private static SingleFilePackageUpdater CreateUpdater(
        RecordingFileDownloader downloader,
        StubDownloadFileMetadataReader metadataReader,
        RecordingArchiveExtractor archiveExtractor)
    {
        return new SingleFilePackageUpdater(
            downloader,
            metadataReader,
            archiveExtractor,
            NullLogger<SingleFilePackageUpdater>.Instance);
    }

    private static PackageUpdatePathSet CreatePackagePaths(TestDirectory testDirectory)
    {
        return TestPackageUpdatePaths.Create(
            TestLauncherPaths.Create(testDirectory),
            _versionRelativePath,
            _versionRelativePath);
    }

    private static RecordingFileDownloader CreateWritingDownloader(string contents)
    {
        return new RecordingFileDownloader
        {
            Handler = (request, cancellationToken) =>
                File.WriteAllTextAsync(request.DestinationFilePath, contents, cancellationToken)
        };
    }

    /// <summary>
    ///     Fails the test if a non-archive download is handed to the extractor.
    /// </summary>
    private static RecordingArchiveExtractor CreateUnusedArchiveExtractor()
    {
        return new RecordingArchiveExtractor
        {
            ExtractHandler = _ => throw new InvalidOperationException(
                "Extraction should not be used for non-archive files.")
        };
    }
}
