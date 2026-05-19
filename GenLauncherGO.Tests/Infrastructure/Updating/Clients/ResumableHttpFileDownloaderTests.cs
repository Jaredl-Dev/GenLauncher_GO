using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Updating.Clients;
using GenLauncherGO.Infrastructure.Updating.Models;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Clients;

public sealed class ResumableHttpFileDownloaderTests
{
    [Theory]
    [InlineData("RelativeUri")]
    [InlineData("UnsupportedScheme")]
    [InlineData("MissingDestination")]
    public async Task DownloadFileAsync_ThrowsForInvalidRequestAsync(string invalidRequest)
    {
        ResumableHttpFileDownloader downloader = CreateDownloader(new QueueHttpMessageHandler());
        DownloadFileRequest request = invalidRequest switch
        {
            "RelativeUri" => new DownloadFileRequest(new Uri("mod.zip", UriKind.Relative), "mod.zip"),
            "UnsupportedScheme" => new DownloadFileRequest(new Uri("ftp://example.test/mod.zip"), "mod.zip"),
            "MissingDestination" => new DownloadFileRequest(new Uri("https://example.test/mod.zip"), " "),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidRequest), invalidRequest, null)
        };
        string expectedParameterName = invalidRequest == "MissingDestination"
            ? "request.DestinationFilePath"
            : "request";

        Func<Task> act = () => downloader.DownloadFileAsync(request, null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(expectedParameterName);
    }

    [Fact]
    public async Task DownloadFileAsync_WritesResponseBodyToDestinationAsync()
    {
        byte[] payload = Encoding.UTF8.GetBytes("download-content");
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.zip");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(HttpStatusCode.OK, payload));
        ResumableHttpFileDownloader downloader = CreateDownloader(handler);
        RecordingProgress<DownloadProgress> progress = new();

        await downloader.DownloadFileAsync(
            new DownloadFileRequest(new Uri("https://example.test/mod.zip"), destinationFilePath),
            progress,
            CancellationToken.None);

        (await File.ReadAllBytesAsync(destinationFilePath)).Should().Equal(payload);
        progress.Reports.Should().Contain(report => report.BytesDownloaded == payload.Length);
    }

    /// <summary>
    ///     The destination is normalized before the transfer starts, so a caller-supplied parent segment resolves to one
    ///     real folder instead of materializing the segment it walks out of.
    /// </summary>
    [Fact]
    public async Task DownloadFileAsync_NormalizesDestinationBeforeCreatingItsFolderAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "a", "..", "nested", "mod.zip");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(HttpStatusCode.OK, Encoding.UTF8.GetBytes("payload")));
        ResumableHttpFileDownloader downloader = CreateDownloader(handler);

        await downloader.DownloadFileAsync(
            new DownloadFileRequest(new Uri("https://example.test/mod.zip"), destinationFilePath),
            null,
            CancellationToken.None);

        (await File.ReadAllTextAsync(Path.Combine(testDirectory.Path, "nested", "mod.zip")))
            .Should().Be("payload");
        Directory.Exists(Path.Combine(testDirectory.Path, "a")).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadFileAsync_ResumesExistingPartialFileWithRangeRequestAsync()
    {
        byte[] partialPayload = Encoding.UTF8.GetBytes("abc");
        byte[] remainingPayload = Encoding.UTF8.GetBytes("def");
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        await File.WriteAllBytesAsync(destinationFilePath, partialPayload);

        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ =>
        {
            HttpResponseMessage response = CreateResponse(HttpStatusCode.PartialContent, remainingPayload);
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 5, 6);
            return response;
        });

        ResumableHttpFileDownloader downloader = CreateDownloader(handler);

        await downloader.DownloadFileAsync(
            new DownloadFileRequest(new Uri("https://example.test/mod.big"), destinationFilePath, 6),
            null,
            CancellationToken.None);

        handler.RangeHeaders.Should().ContainSingle().Which.Should().Be("bytes=3-");
        (await File.ReadAllTextAsync(destinationFilePath)).Should().Be("abcdef");
    }

    /// <summary>
    ///     A range response states the object's real length, so it settles the transfer size even when the caller was
    ///     working from a stale expectation.
    /// </summary>
    [Fact]
    public async Task DownloadFileAsync_ResumedResponseDeclaresTotal_PrefersContentRangeLengthAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        await File.WriteAllTextAsync(destinationFilePath, "abc");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ =>
        {
            HttpResponseMessage response = CreateResponse(
                HttpStatusCode.PartialContent,
                Encoding.UTF8.GetBytes("def"));
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 5, 6);
            return response;
        });
        ResumableHttpFileDownloader downloader = CreateDownloader(handler);
        RecordingProgress<DownloadProgress> progress = new();

        await downloader.DownloadFileAsync(
            new DownloadFileRequest(new Uri("https://example.test/mod.big"), destinationFilePath, 7),
            progress,
            CancellationToken.None);

        handler.RangeHeaders.Should().ContainSingle().Which.Should().Be("bytes=3-");
        (await File.ReadAllTextAsync(destinationFilePath)).Should().Be("abcdef");
        progress.Reports.Last().Should().Be(new DownloadProgress(6, 6, 100));
    }

    [Fact]
    public async Task DownloadFileAsync_RestartsWhenServerIgnoresRangeRequestAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.zip");
        await File.WriteAllTextAsync(destinationFilePath, "partial");

        byte[] fullPayload = Encoding.UTF8.GetBytes("fresh");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(HttpStatusCode.OK, fullPayload));
        ResumableHttpFileDownloader downloader = CreateDownloader(handler);

        await downloader.DownloadFileAsync(
            new DownloadFileRequest(new Uri("https://example.test/mod.zip"), destinationFilePath),
            null,
            CancellationToken.None);

        handler.RangeHeaders.Should().ContainSingle().Which.Should().Be("bytes=7-");
        (await File.ReadAllTextAsync(destinationFilePath)).Should().Be("fresh");
    }

    [Fact]
    public async Task DownloadFileAsync_RestartsWhenServerReturnsUnexpectedContentRangeAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        await File.WriteAllTextAsync(destinationFilePath, "abc");

        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ =>
        {
            HttpResponseMessage response = CreateResponse(HttpStatusCode.PartialContent, Encoding.UTF8.GetBytes("xyz"));
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 2, 6);
            return response;
        });
        handler.Enqueue(_ => CreateResponse(HttpStatusCode.OK, Encoding.UTF8.GetBytes("abcdef")));

        ResumableHttpFileDownloader downloader = CreateDownloader(handler);

        await downloader.DownloadFileAsync(
            new DownloadFileRequest(new Uri("https://example.test/mod.big"), destinationFilePath, 6),
            null,
            CancellationToken.None);

        handler.RangeHeaders.Should().Equal("bytes=3-", null);
        (await File.ReadAllTextAsync(destinationFilePath)).Should().Be("abcdef");
    }

    [Fact]
    public async Task DownloadFileAsync_ReturnsExistingCompleteFileWithoutRequestAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        await File.WriteAllTextAsync(destinationFilePath, "ready");
        QueueHttpMessageHandler handler = new();
        ResumableHttpFileDownloader downloader = CreateDownloader(handler);
        RecordingProgress<DownloadProgress> progress = new();

        await downloader.DownloadFileAsync(
            new DownloadFileRequest(
                new Uri("https://example.test/mod.big"),
                destinationFilePath,
                5),
            progress,
            CancellationToken.None);

        handler.RangeHeaders.Should().BeEmpty();
        progress.Reports.Should().ContainSingle(report =>
            report.TotalBytes == 5 &&
            report.BytesDownloaded == 5 &&
            report.ProgressPercentage == 100);
    }

    [Fact]
    public async Task DownloadFileAsync_ExistingFileLargerThanExpected_RestartsFromZeroAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        await File.WriteAllTextAsync(destinationFilePath, "too-large");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(HttpStatusCode.OK, Encoding.UTF8.GetBytes("fresh")));
        ResumableHttpFileDownloader downloader = CreateDownloader(handler);

        await downloader.DownloadFileAsync(
            new DownloadFileRequest(
                new Uri("https://example.test/mod.big"),
                destinationFilePath,
                5),
            null,
            CancellationToken.None);

        handler.RangeHeaders.Should().ContainSingle().Which.Should().BeNull();
        (await File.ReadAllTextAsync(destinationFilePath)).Should().Be("fresh");
    }

    [Fact]
    public async Task DownloadFileAsync_ReportsProgressWhileContentIsDownloadingAsync()
    {
        byte[] payload = Encoding.UTF8.GetBytes("abcdef");
        ManualTimeProvider timeProvider = new();
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ =>
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StreamContent(new ChunkStream(
                    payload,
                    2,
                    () => timeProvider.Advance(TimeSpan.FromMilliseconds(2))))
            };
            response.Content.Headers.ContentLength = payload.Length;
            return response;
        });
        ResumableHttpFileDownloader downloader = CreateDownloader(handler, timeProvider: timeProvider);
        RecordingProgress<DownloadProgress> progress = new();

        await downloader.DownloadFileAsync(
            new DownloadFileRequest(new Uri("https://example.test/mod.big"), destinationFilePath),
            progress,
            CancellationToken.None);

        long[] reportedBytes = progress.Reports.Select(report => report.BytesDownloaded).ToArray();
        reportedBytes.Should().StartWith(0);
        reportedBytes.Should().Contain(value => value > 0 && value < payload.Length);
        reportedBytes.Should().EndWith(payload.Length);
        reportedBytes.Should().BeInAscendingOrder();
    }

    /// <summary>
    ///     Intermediate progress is rate limited, so a chunked transfer publishes far fewer reports than it reads
    ///     chunks while the opening and final positions still reach the caller.
    /// </summary>
    [Fact]
    public async Task DownloadFileAsync_ThrottlesIntermediateProgressToTheReportIntervalAsync()
    {
        byte[] payload = Encoding.UTF8.GetBytes("abcdef");
        ManualTimeProvider timeProvider = new();
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ =>
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StreamContent(new ChunkStream(
                    payload,
                    1,
                    () => timeProvider.Advance(TimeSpan.FromMilliseconds(2))))
            };
            response.Content.Headers.ContentLength = payload.Length;
            return response;
        });
        ResumableHttpFileDownloader downloader = CreateDownloader(
            handler,
            progressReportInterval: TimeSpan.FromMilliseconds(10),
            timeProvider: timeProvider);
        RecordingProgress<DownloadProgress> progress = new();

        await downloader.DownloadFileAsync(
            new DownloadFileRequest(new Uri("https://example.test/mod.big"), destinationFilePath),
            progress,
            CancellationToken.None);

        long[] reportedBytes = progress.Reports.Select(report => report.BytesDownloaded).ToArray();
        reportedBytes.Should().StartWith(0);
        reportedBytes.Should().EndWith(payload.Length);
        reportedBytes.Should().HaveCountLessThan(payload.Length);
    }

    [Fact]
    public async Task DownloadFileAsync_PauseStopsTransferUntilResumedAsync()
    {
        byte[] payload = Encoding.UTF8.GetBytes("abcdef");
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        SignalingStreamContent responseContent = new(payload);
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = responseContent });
        ResumableHttpFileDownloader downloader = CreateDownloader(handler);
        PackageDownloadPauseController pauseController = new();
        pauseController.Pause().Should().BeTrue();

        Task download = downloader.DownloadFileAsync(
            new DownloadFileRequest(
                new Uri("https://example.test/mod.big"),
                destinationFilePath,
                PauseController: pauseController),
            null,
            CancellationToken.None);
        await responseContent.BodyRequested.WaitAsync(TestTimeouts.Wait);

        download.IsCompleted.Should().BeFalse();
        new FileInfo(destinationFilePath).Length.Should().Be(0);

        pauseController.Resume().Should().BeTrue();
        await download.WaitAsync(TestTimeouts.Wait);

        (await File.ReadAllBytesAsync(destinationFilePath)).Should().Equal(payload);
    }

    [Fact]
    public async Task DownloadFileAsync_CancellationInterruptsPausedTransferAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        SignalingStreamContent responseContent = new(Encoding.UTF8.GetBytes("abcdef"));
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = responseContent });
        ResumableHttpFileDownloader downloader = CreateDownloader(handler);
        PackageDownloadPauseController pauseController = new();
        pauseController.Pause();
        using CancellationTokenSource cancellation = new();

        Task download = downloader.DownloadFileAsync(
            new DownloadFileRequest(
                new Uri("https://example.test/mod.big"),
                destinationFilePath,
                PauseController: pauseController),
            null,
            cancellation.Token);
        await responseContent.BodyRequested.WaitAsync(TestTimeouts.Wait);
        await cancellation.CancelAsync();

        Func<Task> act = () => download;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DownloadFileAsync_ResumesBytesWrittenBeforeTransientResponseFailureAsync()
    {
        byte[] partialPayload = Encoding.UTF8.GetBytes("abc");
        byte[] remainingPayload = Encoding.UTF8.GetBytes("def");
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new FailingAfterPayloadStream(partialPayload))
        });
        handler.Enqueue(_ =>
        {
            HttpResponseMessage response = CreateResponse(HttpStatusCode.PartialContent, remainingPayload);
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 5, 6);
            return response;
        });
        ResumableHttpFileDownloader downloader = CreateDownloader(handler);

        await downloader.DownloadFileAsync(
            new DownloadFileRequest(
                new Uri("https://example.test/mod.big"),
                destinationFilePath,
                6),
            null,
            CancellationToken.None);

        handler.RangeHeaders.Should().Equal(null, "bytes=3-");
        (await File.ReadAllTextAsync(destinationFilePath)).Should().Be("abcdef");
    }

    [Fact]
    public async Task DownloadFileAsync_CancellationRetainsWrittenPrefixForResumeAsync()
    {
        byte[] partialPayload = Encoding.UTF8.GetBytes("abc");
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        WaitingAfterPayloadStream responseStream = new(partialPayload);
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(responseStream)
        });
        ResumableHttpFileDownloader downloader = CreateDownloader(handler);
        using CancellationTokenSource cancellation = new();

        Task download = downloader.DownloadFileAsync(
            new DownloadFileRequest(
                new Uri("https://example.test/mod.big"),
                destinationFilePath,
                6),
            null,
            cancellation.Token);
        await responseStream.WaitingForCancellation.WaitAsync(TestTimeouts.Wait);
        await cancellation.CancelAsync();

        Func<Task> act = () => download;
        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.RangeHeaders.Should().ContainSingle().Which.Should().BeNull();
        (await File.ReadAllTextAsync(destinationFilePath)).Should().Be("abc");
    }

    [Fact]
    public async Task DownloadFileAsync_ThrowsAfterFinalRetriableFailureAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => throw new HttpRequestException("offline"));
        handler.Enqueue(_ => throw new HttpRequestException("still offline"));
        ResumableHttpFileDownloader downloader = CreateDownloader(handler);

        Func<Task> act = () => downloader.DownloadFileAsync(
            new DownloadFileRequest(new Uri("https://example.test/mod.big"), destinationFilePath),
            null,
            CancellationToken.None);

        IOException exception = (await act.Should().ThrowAsync<IOException>()
            .WithMessage("Download failed after 2 attempts.")).Which;
        exception.InnerException.Should().BeOfType<HttpRequestException>();
        handler.RangeHeaders.Should().HaveCount(2);
    }

    [Fact]
    public async Task DownloadFileAsync_ThrowsWhenDownloadedBytesDoNotMatchExpectedBytesAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(HttpStatusCode.OK, Encoding.UTF8.GetBytes("abc")));
        ResumableHttpFileDownloader downloader = CreateDownloader(handler, 1);

        Func<Task> act = () => downloader.DownloadFileAsync(
            new DownloadFileRequest(
                new Uri("https://example.test/mod.big"),
                destinationFilePath,
                6),
            null,
            CancellationToken.None);

        IOException exception = (await act.Should().ThrowAsync<IOException>()
            .WithMessage("Download failed after 1 attempts.")).Which;
        exception.InnerException.Should().BeOfType<IOException>()
            .Which.Message.Should().Be("Downloaded 3 bytes, but expected 6 bytes.");
    }

    [Fact]
    public async Task DownloadFileAsync_ThrowsWhenTransferStallsAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BlockingStream())
        });
        ResumableHttpFileDownloader downloader = CreateDownloader(
            handler,
            1,
            TimeSpan.FromMilliseconds(10));

        Func<Task> act = () => downloader.DownloadFileAsync(
            new DownloadFileRequest(new Uri("https://example.test/mod.big"), destinationFilePath),
            null,
            CancellationToken.None);

        IOException exception = (await act.Should().ThrowAsync<IOException>()
            .WithMessage("Download failed after 1 attempts.")).Which;
        exception.InnerException.Should().BeOfType<TimeoutException>();
    }

    [Fact]
    public async Task DownloadFileAsync_WhenResumeIsDisabled_ReplacesExistingFileAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        await File.WriteAllTextAsync(destinationFilePath, "stale");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(HttpStatusCode.OK, Encoding.UTF8.GetBytes("fresh")));
        ResumableHttpFileDownloader downloader = CreateDownloader(handler);

        await downloader.DownloadFileAsync(
            new DownloadFileRequest(
                new Uri("https://example.test/mod.big"),
                destinationFilePath,
                Resume: false),
            null,
            CancellationToken.None);

        handler.RangeHeaders.Should().ContainSingle().Which.Should().BeNull();
        (await File.ReadAllTextAsync(destinationFilePath)).Should().Be("fresh");
    }

    [Fact]
    public async Task DownloadFileAsync_PartialResponseForNewFile_UsesResponseLengthAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ =>
        {
            HttpResponseMessage response = CreateResponse(
                HttpStatusCode.PartialContent,
                Encoding.UTF8.GetBytes("abc"));
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 2, 6);
            return response;
        });
        ResumableHttpFileDownloader downloader = CreateDownloader(handler);

        await downloader.DownloadFileAsync(
            new DownloadFileRequest(new Uri("https://example.test/mod.big"), destinationFilePath),
            null,
            CancellationToken.None);

        handler.RangeHeaders.Should().ContainSingle().Which.Should().BeNull();
        (await File.ReadAllTextAsync(destinationFilePath)).Should().Be("abc");
    }

    [Fact]
    public async Task DownloadFileAsync_ResumedResponseWithoutDeclaredTotal_CombinesExistingAndResponseBytesAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        await File.WriteAllTextAsync(destinationFilePath, "abc");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ =>
        {
            HttpResponseMessage response = CreateResponse(
                HttpStatusCode.PartialContent,
                Encoding.UTF8.GetBytes("def"));
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 5);
            return response;
        });
        RecordingProgress<DownloadProgress> progress = new();
        ResumableHttpFileDownloader downloader = CreateDownloader(handler);

        await downloader.DownloadFileAsync(
            new DownloadFileRequest(new Uri("https://example.test/mod.big"), destinationFilePath),
            progress,
            CancellationToken.None);

        handler.RangeHeaders.Should().ContainSingle().Which.Should().Be("bytes=3-");
        (await File.ReadAllTextAsync(destinationFilePath)).Should().Be("abcdef");
        progress.Reports.First().Should().Be(new DownloadProgress(6, 3, 50));
        progress.Reports.Last().Should().Be(new DownloadProgress(6, 6, 100));
    }

    [Fact]
    public async Task DownloadFileAsync_NonRetriableFailure_DoesNotRetryAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => throw new InvalidOperationException());
        handler.Enqueue(_ => CreateResponse(HttpStatusCode.OK, Encoding.UTF8.GetBytes("unexpected")));
        ResumableHttpFileDownloader downloader = CreateDownloader(handler);

        Func<Task> act = () => downloader.DownloadFileAsync(
            new DownloadFileRequest(new Uri("https://example.test/mod.big"), destinationFilePath),
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().ContainSingle();
        File.Exists(destinationFilePath).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadFileAsync_PreCanceledToken_DoesNotSendRequestAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(HttpStatusCode.OK, Encoding.UTF8.GetBytes("unexpected")));
        ResumableHttpFileDownloader downloader = CreateDownloader(handler);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> act = () => downloader.DownloadFileAsync(
            new DownloadFileRequest(new Uri("https://example.test/mod.big"), destinationFilePath),
            null,
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().BeEmpty();
        File.Exists(destinationFilePath).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadFileAsync_EmptyResponse_ReportsIndeterminatePercentageAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(HttpStatusCode.OK, []));
        RecordingProgress<DownloadProgress> progress = new();
        ResumableHttpFileDownloader downloader = CreateDownloader(handler);

        await downloader.DownloadFileAsync(
            new DownloadFileRequest(new Uri("https://example.test/mod.big"), destinationFilePath),
            progress,
            CancellationToken.None);

        new FileInfo(destinationFilePath).Length.Should().Be(0);
        progress.Reports.Should().NotBeEmpty();
        progress.Reports.Should().OnlyContain(report =>
            report.TotalBytes == 0 &&
            report.BytesDownloaded == 0 &&
            report.ProgressPercentage == null);
    }

    /// <summary>
    ///     A server that refuses the resume range answers 416. The bytes already on disk are not the whole
    ///     file, so the transfer has to fail: reporting it complete would install truncated content as though
    ///     it were whole, and the launch that follows would run against it.
    /// </summary>
    [Fact]
    public async Task DownloadFileAsync_ServerRejectsTheResumeRange_FailsAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        await File.WriteAllTextAsync(destinationFilePath, "abc");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
        ResumableHttpFileDownloader downloader = CreateDownloader(handler, maxAttempts: 1);

        Func<Task> download = () => downloader.DownloadFileAsync(
            new DownloadFileRequest(new Uri("https://example.test/mod.big"), destinationFilePath, 6),
            null,
            CancellationToken.None);

        await download.Should().ThrowAsync<IOException>();
    }

    private static ResumableHttpFileDownloader CreateDownloader(
        QueueHttpMessageHandler handler,
        int maxAttempts = 2,
        TimeSpan? idleTimeout = null,
        TimeSpan? progressReportInterval = null,
        TimeProvider? timeProvider = null)
    {
        HttpClient httpClient = new(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        return new ResumableHttpFileDownloader(
            httpClient,
            null,
            4,
            maxAttempts,
            idleTimeout ?? TimeSpan.FromSeconds(5),
            progressReportInterval ?? TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(1),
            timeProvider);
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, byte[] payload)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(payload)
        };
    }

    /// <summary>
    ///     Reports when the transfer first reaches for the response body, which happens only after the destination file
    ///     has been opened, so a paused transfer can be observed without polling.
    /// </summary>
    private sealed class SignalingStreamContent : StreamContent
    {
        private readonly TaskCompletionSource _bodyRequested =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SignalingStreamContent(byte[] payload)
            : base(new MemoryStream(payload, false))
        {
        }

        public Task BodyRequested => _bodyRequested.Task;

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            _bodyRequested.TrySetResult();
            return base.CreateContentReadStreamAsync();
        }

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        {
            _bodyRequested.TrySetResult();
            return base.CreateContentReadStreamAsync(cancellationToken);
        }
    }

    private sealed class ChunkStream : Stream
    {
        private readonly Action _beforeRead;
        private readonly int _chunkSize;
        private readonly byte[] _payload;
        private int _position;

        public ChunkStream(byte[] payload, int chunkSize, Action beforeRead)
        {
            _payload = payload;
            _chunkSize = chunkSize;
            _beforeRead = beforeRead;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _payload.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_position >= _payload.Length)
            {
                return ValueTask.FromResult(0);
            }

            cancellationToken.ThrowIfCancellationRequested();
            _beforeRead();
            int bytesToCopy = Math.Min(Math.Min(_chunkSize, buffer.Length), _payload.Length - _position);
            _payload.AsMemory(_position, bytesToCopy).CopyTo(buffer);
            _position += bytesToCopy;
            return ValueTask.FromResult(bytesToCopy);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FailingAfterPayloadStream : Stream
    {
        private readonly byte[] _payload;
        private int _position;

        public FailingAfterPayloadStream(byte[] payload)
        {
            _payload = payload;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _payload.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_position >= _payload.Length)
            {
                throw new IOException("response interrupted");
            }

            int bytesToCopy = Math.Min(buffer.Length, _payload.Length - _position);
            _payload.AsMemory(_position, bytesToCopy).CopyTo(buffer);
            _position += bytesToCopy;
            return ValueTask.FromResult(bytesToCopy);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class WaitingAfterPayloadStream : Stream
    {
        private readonly byte[] _payload;
        private readonly TaskCompletionSource _waitingForCancellation =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _position;

        public WaitingAfterPayloadStream(byte[] payload)
        {
            _payload = payload;
        }

        public Task WaitingForCancellation => _waitingForCancellation.Task;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _payload.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_position < _payload.Length)
            {
                int bytesToCopy = Math.Min(buffer.Length, _payload.Length - _position);
                _payload.AsMemory(_position, bytesToCopy).CopyTo(buffer);
                _position += bytesToCopy;
                return bytesToCopy;
            }

            _waitingForCancellation.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class BlockingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
