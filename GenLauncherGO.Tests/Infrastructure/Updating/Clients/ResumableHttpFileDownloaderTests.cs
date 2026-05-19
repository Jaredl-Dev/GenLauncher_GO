using System;
using System.Collections.Generic;
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
using GenLauncherGO.Tests.Testing;

namespace GenLauncherGO.Tests.Infrastructure.Updating.Clients;

public sealed class ResumableHttpFileDownloaderTests
{
    [Theory]
    [InlineData("RelativeUri")]
    [InlineData("UnsupportedScheme")]
    [InlineData("MissingDestination")]
    public async Task DownloadFileAsyncThrowsForInvalidRequestAsync(string invalidRequest)
    {
        ResumableHttpFileDownloader downloader = CreateDownloader(new QueueHttpMessageHandler());
        DownloadFileRequest request = invalidRequest switch
        {
            "RelativeUri" => new DownloadFileRequest(new Uri("mod.zip", UriKind.Relative), "mod.zip"),
            "UnsupportedScheme" => new DownloadFileRequest(new Uri("ftp://example.test/mod.zip"), "mod.zip"),
            "MissingDestination" => new DownloadFileRequest(new Uri("https://example.test/mod.zip"), " "),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidRequest), invalidRequest, null),
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

        File.ReadAllBytes(destinationFilePath).Should().Equal(payload);
        progress.Reports.Should().Contain(report => report.BytesDownloaded == payload.Length);
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
            new DownloadFileRequest(new Uri("https://example.test/mod.big"), destinationFilePath, ExpectedBytes: 6),
            null,
            CancellationToken.None);

        handler.RangeHeaders.Should().ContainSingle().Which.Should().Be("bytes=3-");
        File.ReadAllText(destinationFilePath).Should().Be("abcdef");
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
        File.ReadAllText(destinationFilePath).Should().Be("fresh");
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
            new DownloadFileRequest(new Uri("https://example.test/mod.big"), destinationFilePath, ExpectedBytes: 6),
            null,
            CancellationToken.None);

        handler.RangeHeaders.Should().Equal("bytes=3-", null);
        File.ReadAllText(destinationFilePath).Should().Be("abcdef");
    }

    [Fact]
    public async Task DownloadFileAsyncReturnsExistingCompleteFileWithoutRequestAsync()
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
                ExpectedBytes: 5),
            progress,
            CancellationToken.None);

        handler.RangeHeaders.Should().BeEmpty();
        progress.Reports.Should().ContainSingle(report =>
            report.TotalBytes == 5 &&
            report.BytesDownloaded == 5 &&
            report.ProgressPercentage == 100);
    }

    [Fact]
    public async Task DownloadFileAsyncRestartsWhenExistingFileIsLargerThanExpectedAsync()
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
                ExpectedBytes: 5),
            null,
            CancellationToken.None);

        handler.RangeHeaders.Should().ContainSingle().Which.Should().BeNull();
        File.ReadAllText(destinationFilePath).Should().Be("fresh");
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
                    chunkSize: 2,
                    () => timeProvider.Advance(TimeSpan.FromMilliseconds(2)))),
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

    [Fact]
    public async Task DownloadFileAsync_PauseStopsTransferUntilResumedAsync()
    {
        byte[] payload = Encoding.UTF8.GetBytes("abcdef");
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(HttpStatusCode.OK, payload));
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
        while (handler.Requests.Count == 0)
        {
            await Task.Yield();
        }

        download.IsCompleted.Should().BeFalse();
        new FileInfo(destinationFilePath).Length.Should().Be(0);

        pauseController.Resume().Should().BeTrue();
        await download.WaitAsync(TimeSpan.FromSeconds(5));

        File.ReadAllBytes(destinationFilePath).Should().Equal(payload);
    }

    [Fact]
    public async Task DownloadFileAsync_CancellationInterruptsPausedTransferAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(HttpStatusCode.OK, Encoding.UTF8.GetBytes("abcdef")));
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
        while (handler.Requests.Count == 0)
        {
            await Task.Yield();
        }

        await cancellation.CancelAsync();

        Func<Task> act = () => download;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DownloadFileAsyncThrowsAfterFinalRetriableFailureAsync()
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
    public async Task DownloadFileAsyncThrowsWhenDownloadedBytesDoNotMatchExpectedBytesAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(HttpStatusCode.OK, Encoding.UTF8.GetBytes("abc")));
        ResumableHttpFileDownloader downloader = CreateDownloader(handler, maxAttempts: 1);

        Func<Task> act = () => downloader.DownloadFileAsync(
            new DownloadFileRequest(
                new Uri("https://example.test/mod.big"),
                destinationFilePath,
                ExpectedBytes: 6),
            null,
            CancellationToken.None);

        IOException exception = (await act.Should().ThrowAsync<IOException>()
            .WithMessage("Download failed after 1 attempts.")).Which;
        exception.InnerException.Should().BeOfType<IOException>()
            .Which.Message.Should().Be("Downloaded 3 bytes, but expected 6 bytes.");
    }

    [Fact]
    public async Task DownloadFileAsyncThrowsWhenTransferStallsAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BlockingStream()),
        });
        ResumableHttpFileDownloader downloader = CreateDownloader(
            handler,
            maxAttempts: 1,
            idleTimeout: TimeSpan.FromMilliseconds(10));

        Func<Task> act = () => downloader.DownloadFileAsync(
            new DownloadFileRequest(new Uri("https://example.test/mod.big"), destinationFilePath),
            null,
            CancellationToken.None);

        IOException exception = (await act.Should().ThrowAsync<IOException>()
            .WithMessage("Download failed after 1 attempts.")).Which;
        exception.InnerException.Should().BeOfType<TimeoutException>();
    }

    private static ResumableHttpFileDownloader CreateDownloader(
        QueueHttpMessageHandler handler,
        int maxAttempts = 2,
        TimeSpan? idleTimeout = null,
        TimeProvider? timeProvider = null)
    {
        HttpClient httpClient = new(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        return new ResumableHttpFileDownloader(
            httpClient,
            logger: null,
            bufferSize: 4,
            maxAttempts: maxAttempts,
            idleTimeout: idleTimeout ?? TimeSpan.FromSeconds(5),
            progressReportInterval: TimeSpan.FromMilliseconds(1),
            initialRetryDelay: TimeSpan.FromMilliseconds(1),
            timeProvider);
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, byte[] payload)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(payload),
        };
    }

    private sealed class ChunkStream : Stream
    {
        private readonly Action _beforeRead;
        private readonly byte[] _payload;
        private readonly int _chunkSize;
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

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        private readonly List<T> _reports = new();

        public IReadOnlyList<T> Reports => _reports;

        public void Report(T value)
        {
            _reports.Add(value);
        }
    }
}
