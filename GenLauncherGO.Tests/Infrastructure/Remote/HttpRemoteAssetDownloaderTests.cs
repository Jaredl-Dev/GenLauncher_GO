using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Remote;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.Infrastructure.Remote;

public sealed class HttpRemoteAssetDownloaderTests
{
    [Fact]
    public async Task DownloadIfMissingAsync_DeletesStaleTemporaryFileAndDoesNotResumeAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "asset.png");
        string temporaryFilePath = destinationFilePath + ".download";
        await File.WriteAllTextAsync(temporaryFilePath, "stale");
        RecordingFileDownloader fileDownloader = new()
        {
            Handler = (request, cancellationToken) =>
                File.WriteAllTextAsync(request.DestinationFilePath, "fresh", cancellationToken)
        };
        HttpRemoteAssetDownloader downloader = CreateDownloader(fileDownloader);

        await downloader.DownloadIfMissingAsync(
            new Uri("https://example.test/asset.png"),
            destinationFilePath,
            CancellationToken.None);

        fileDownloader.Requests.Should().ContainSingle()
            .Which.Resume.Should().BeFalse();
        (await File.ReadAllTextAsync(destinationFilePath)).Should().Be("fresh");
        File.Exists(temporaryFilePath).Should().BeFalse();
    }

    /// <summary>
    ///     The transfer never resumes, so leftover bytes from an interrupted session have to be gone before the next
    ///     one starts writing into the same staging file.
    /// </summary>
    [Fact]
    public async Task DownloadIfMissingAsync_StaleTemporaryFile_IsGoneWhenTheDownloadStartsAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "asset.png");
        await File.WriteAllTextAsync(destinationFilePath + ".download", "stale");
        bool? staleFileExistedAtDownload = null;
        RecordingFileDownloader fileDownloader = new()
        {
            Handler = (request, cancellationToken) =>
            {
                staleFileExistedAtDownload = File.Exists(request.DestinationFilePath);
                return File.WriteAllTextAsync(request.DestinationFilePath, "fresh", cancellationToken);
            }
        };
        HttpRemoteAssetDownloader downloader = CreateDownloader(fileDownloader);

        await downloader.DownloadIfMissingAsync(
            new Uri("https://example.test/asset.png"),
            destinationFilePath,
            CancellationToken.None);

        staleFileExistedAtDownload.Should().BeFalse();
    }

    /// <summary>
    ///     Remote assets are cached below folders the launcher creates on demand, so the destination folder cannot be
    ///     assumed to exist when the download is requested.
    /// </summary>
    [Fact]
    public async Task DownloadIfMissingAsync_MissingDestinationDirectory_IsCreatedBeforeDownloadingAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "Images", "Contra", "asset.png");
        RecordingFileDownloader fileDownloader = new()
        {
            Handler = (request, cancellationToken) =>
                File.WriteAllTextAsync(request.DestinationFilePath, "fresh", cancellationToken)
        };
        HttpRemoteAssetDownloader downloader = CreateDownloader(fileDownloader);

        await downloader.DownloadIfMissingAsync(
            new Uri("https://example.test/asset.png"),
            destinationFilePath,
            CancellationToken.None);

        (await File.ReadAllTextAsync(destinationFilePath)).Should().Be("fresh");
    }

    /// <summary>
    ///     Closing the launcher cancels asset downloads mid-transfer. The partially staged file must never be published
    ///     as the finished asset, because a present destination is what stops the next session downloading it again.
    /// </summary>
    [Fact]
    public async Task DownloadIfMissingAsync_CanceledDuringDownload_LeavesTheDestinationMissingAsync()
    {
        using TestDirectory testDirectory = new();
        using CancellationTokenSource cancellation = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "asset.png");
        RecordingFileDownloader fileDownloader = new()
        {
            Handler = async (request, cancellationToken) =>
            {
                await File.WriteAllTextAsync(request.DestinationFilePath, "partial", cancellationToken);
                await cancellation.CancelAsync();
            }
        };
        HttpRemoteAssetDownloader downloader = CreateDownloader(fileDownloader);

        Func<Task> act = () => downloader.DownloadIfMissingAsync(
            new Uri("https://example.test/asset.png"),
            destinationFilePath,
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(destinationFilePath).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadIfMissingAsync_ExistingDestination_KeepsContentsWithoutDownloadingAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = testDirectory.CreateFile("asset.png", "already downloaded");
        RecordingFileDownloader fileDownloader = new();
        HttpRemoteAssetDownloader downloader = CreateDownloader(fileDownloader);

        await downloader.DownloadIfMissingAsync(
            new Uri("https://example.test/asset.png"),
            destinationFilePath,
            CancellationToken.None);

        fileDownloader.Requests.Should().BeEmpty();
        (await File.ReadAllTextAsync(destinationFilePath)).Should().Be("already downloaded");
    }

    /// <summary>
    ///     Another session can finish the same asset while this download runs, so the completed destination must win
    ///     over the temporary file this call staged.
    /// </summary>
    [Fact]
    public async Task DownloadIfMissingAsync_DestinationAppearsDuringDownload_KeepsItAndRemovesTemporaryFileAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "asset.png");
        string temporaryFilePath = destinationFilePath + ".download";
        RecordingFileDownloader fileDownloader = new()
        {
            Handler = async (request, cancellationToken) =>
            {
                await File.WriteAllTextAsync(request.DestinationFilePath, "this download", cancellationToken);
                await File.WriteAllTextAsync(destinationFilePath, "the other session", cancellationToken);
            }
        };
        HttpRemoteAssetDownloader downloader = CreateDownloader(fileDownloader);

        await downloader.DownloadIfMissingAsync(
            new Uri("https://example.test/asset.png"),
            destinationFilePath,
            CancellationToken.None);

        (await File.ReadAllTextAsync(destinationFilePath)).Should().Be("the other session");
        File.Exists(temporaryFilePath).Should().BeFalse();
    }

    private static HttpRemoteAssetDownloader CreateDownloader(RecordingFileDownloader fileDownloader)
    {
        return new HttpRemoteAssetDownloader(
            fileDownloader,
            NullLogger<HttpRemoteAssetDownloader>.Instance);
    }
}
