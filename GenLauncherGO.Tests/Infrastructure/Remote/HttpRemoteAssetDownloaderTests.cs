using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Remote;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using GenLauncherGO.Tests.Testing;
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

        RecordingFileDownloader fileDownloader = new();
        HttpRemoteAssetDownloader downloader = new(
            fileDownloader,
            NullLogger<HttpRemoteAssetDownloader>.Instance);

        await downloader.DownloadIfMissingAsync(
            new Uri("https://example.test/asset.png"),
            destinationFilePath,
            CancellationToken.None);

        fileDownloader.Requests.Should().ContainSingle()
            .Which.Resume.Should().BeFalse();
        File.ReadAllText(destinationFilePath).Should().Be("fresh");
        File.Exists(temporaryFilePath).Should().BeFalse();
    }

    private sealed class RecordingFileDownloader : IResumableFileDownloader
    {
        public List<DownloadFileRequest> Requests { get; } = new();

        public async Task DownloadFileAsync(
            DownloadFileRequest request,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            await File.WriteAllTextAsync(request.DestinationFilePath, "fresh", cancellationToken);
        }
    }
}
