using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Features.Updating;

namespace GenLauncherGO.Tests.Features.Updating;

public sealed class ResumableHttpFileDownloaderTests
{
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

        (await File.ReadAllBytesAsync(destinationFilePath, TestContext.Current.CancellationToken)).Should().Equal(payload);
        progress.Reports.Should().Contain(report => report.BytesDownloaded == payload.Length);
    }

    [Fact]
    public async Task DownloadFileAsync_ResumesExistingPartialFileWithRangeRequestAsync()
    {
        byte[] partialPayload = Encoding.UTF8.GetBytes("abc");
        byte[] remainingPayload = Encoding.UTF8.GetBytes("def");
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        await File.WriteAllBytesAsync(destinationFilePath, partialPayload, TestContext.Current.CancellationToken);

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
        (await File.ReadAllTextAsync(destinationFilePath, TestContext.Current.CancellationToken)).Should().Be("abcdef");
    }

    [Fact]
    public async Task DownloadFileAsync_RestartsWhenServerIgnoresRangeRequestAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.zip");
        await File.WriteAllTextAsync(destinationFilePath, "partial", TestContext.Current.CancellationToken);

        byte[] fullPayload = Encoding.UTF8.GetBytes("fresh");
        QueueHttpMessageHandler handler = new();
        handler.Enqueue(_ => CreateResponse(HttpStatusCode.OK, fullPayload));
        ResumableHttpFileDownloader downloader = CreateDownloader(handler);

        await downloader.DownloadFileAsync(
            new DownloadFileRequest(new Uri("https://example.test/mod.zip"), destinationFilePath),
            null,
            CancellationToken.None);

        handler.RangeHeaders.Should().ContainSingle().Which.Should().Be("bytes=7-");
        (await File.ReadAllTextAsync(destinationFilePath, TestContext.Current.CancellationToken)).Should().Be("fresh");
    }

    [Fact]
    public async Task DownloadFileAsync_RestartsWhenServerReturnsUnexpectedContentRangeAsync()
    {
        using TestDirectory testDirectory = new();
        string destinationFilePath = Path.Combine(testDirectory.Path, "mod.big");
        await File.WriteAllTextAsync(destinationFilePath, "abc", TestContext.Current.CancellationToken);

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
        (await File.ReadAllTextAsync(destinationFilePath, TestContext.Current.CancellationToken)).Should().Be("abcdef");
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
        await responseContent.BodyRequested.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        download.IsCompleted.Should().BeFalse();
        new FileInfo(destinationFilePath).Length.Should().Be(0);

        pauseController.Resume().Should().BeTrue();
        await download.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        (await File.ReadAllBytesAsync(destinationFilePath, TestContext.Current.CancellationToken)).Should().Equal(payload);
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
        await responseContent.BodyRequested.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
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
        (await File.ReadAllTextAsync(destinationFilePath, TestContext.Current.CancellationToken)).Should().Be("abcdef");
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
        await responseStream.WaitingForCancellation.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        Func<Task> act = () => download;
        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.RangeHeaders.Should().ContainSingle().Which.Should().BeNull();
        (await File.ReadAllTextAsync(destinationFilePath, TestContext.Current.CancellationToken)).Should().Be("abc");
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
        await File.WriteAllTextAsync(destinationFilePath, "abc", TestContext.Current.CancellationToken);
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
        TimeSpan? idleTimeout = null)
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
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(1));
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
