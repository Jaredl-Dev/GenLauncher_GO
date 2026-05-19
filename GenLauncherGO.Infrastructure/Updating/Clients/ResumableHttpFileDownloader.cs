using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Updating.Models;
using GenLauncherGO.Infrastructure.Remote;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Infrastructure.Updating.Clients;

/// <summary>
///     Downloads files over HTTP using range requests, pooled buffers, retry backoff, and idle-transfer detection.
/// </summary>
internal sealed class ResumableHttpFileDownloader : IResumableFileDownloader
{
    private const int DefaultBufferSize = 1024 * 1024;
    private const int DefaultMaxAttempts = 5;

    private static readonly HttpClient _sharedHttpClient =
        SharedHttpClientFactory.Create(Timeout.InfiniteTimeSpan);

    private readonly int _bufferSize;

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _initialRetryDelay;
    private readonly ILogger<ResumableHttpFileDownloader> _logger;
    private readonly int _maxAttempts;
    private readonly TimeSpan _progressReportInterval;
    private readonly TimeProvider _timeProvider;

    public ResumableHttpFileDownloader(
        HttpClient? httpClient = null,
        ILogger<ResumableHttpFileDownloader>? logger = null)
        : this(
            httpClient,
            logger,
            DefaultBufferSize,
            DefaultMaxAttempts,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(1))
    {
    }

    internal ResumableHttpFileDownloader(
        HttpClient? httpClient,
        ILogger<ResumableHttpFileDownloader>? logger,
        int bufferSize,
        int maxAttempts,
        TimeSpan idleTimeout,
        TimeSpan progressReportInterval,
        TimeSpan initialRetryDelay,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? _sharedHttpClient;
        _logger = logger ?? NullLogger<ResumableHttpFileDownloader>.Instance;
        _bufferSize = bufferSize;
        _maxAttempts = maxAttempts;
        _idleTimeout = idleTimeout;
        _progressReportInterval = progressReportInterval;
        _initialRetryDelay = initialRetryDelay;
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (_bufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferSize),
                "Download buffer size must be greater than zero.");
        }

        if (_maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Maximum attempts must be greater than zero.");
        }

        if (_idleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleTimeout), "Idle timeout must be greater than zero.");
        }

        if (_progressReportInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(progressReportInterval),
                "Progress report interval must be greater than zero.");
        }
    }

    public async Task DownloadFileAsync(
        DownloadFileRequest request,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.SourceUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Download source URI must be absolute.", nameof(request));
        }

        if (!string.Equals(request.SourceUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.SourceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Download source URI must use HTTP or HTTPS.", nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationFilePath);

        string destinationFilePath = LexicalPath.NormalizeFullPath(request.DestinationFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath) ?? ".");

        Exception? lastException = null;
        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await DownloadAttemptAsync(
                    request with { DestinationFilePath = destinationFilePath },
                    progress,
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsRetriable(exception) && attempt < _maxAttempts)
            {
                lastException = exception;
                _logger.LogWarning(
                    exception,
                    "Download attempt {Attempt} failed for {FileName}; retrying.",
                    attempt,
                    Path.GetFileName(destinationFilePath));

                await Task.Delay(GetRetryDelay(attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsRetriable(exception))
            {
                lastException = exception;
                break;
            }
        }

        throw new IOException(
            string.Format(
                CultureInfo.InvariantCulture,
                "Download failed after {0} attempts.",
                _maxAttempts),
            lastException);
    }

    private async Task DownloadAttemptAsync(
        DownloadFileRequest request,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        long existingBytes = GetExistingBytes(request);
        if (request.ExpectedBytes.HasValue && existingBytes == request.ExpectedBytes.Value)
        {
            ReportProgress(progress, request.ExpectedBytes, existingBytes);
            return;
        }

        using HttpRequestMessage message = new(HttpMethod.Get, request.SourceUri);
        if (existingBytes > 0)
        {
            message.Headers.Range = new RangeHeaderValue(existingBytes, null);
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        bool partialContentResponse = response.StatusCode == HttpStatusCode.PartialContent;
        bool serverAcceptedResume = existingBytes > 0 &&
                                    partialContentResponse &&
                                    ResponseStartsAtExpectedByte(response, existingBytes);
        if (existingBytes > 0 && partialContentResponse && !serverAcceptedResume)
        {
            _logger.LogWarning(
                "Server returned an unexpected byte range for {FileName}; restarting download from byte zero.",
                Path.GetFileName(request.DestinationFilePath));

            File.Delete(request.DestinationFilePath);
            throw new IOException("Server returned an unexpected byte range for a resumed download.");
        }

        if (existingBytes > 0 && !serverAcceptedResume)
        {
            _logger.LogInformation(
                "Server did not honor range request for {FileName}; restarting from byte zero.",
                Path.GetFileName(request.DestinationFilePath));

            existingBytes = 0;
        }

        long? totalBytes = ResolveTotalBytes(request, response, existingBytes, serverAcceptedResume);
        FileMode fileMode = existingBytes > 0 && serverAcceptedResume ? FileMode.Append : FileMode.Create;
        long bytesDownloaded = existingBytes;
        ReportProgress(progress, totalBytes, bytesDownloaded);

        await using FileStream destinationStream = new(
            request.DestinationFilePath,
            fileMode,
            FileAccess.Write,
            FileShare.Read,
            _bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        bytesDownloaded = await CopyToFileAsync(
            responseStream,
            destinationStream,
            totalBytes,
            bytesDownloaded,
            progress,
            request.PauseController,
            cancellationToken).ConfigureAwait(false);

        if (totalBytes.HasValue && bytesDownloaded != totalBytes.Value)
        {
            throw new IOException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Downloaded {0} bytes, but expected {1} bytes.",
                    bytesDownloaded,
                    totalBytes.Value));
        }
    }

    private long GetExistingBytes(DownloadFileRequest request)
    {
        if (!request.Resume || !File.Exists(request.DestinationFilePath))
        {
            return 0;
        }

        long existingBytes = new FileInfo(request.DestinationFilePath).Length;
        if (request.ExpectedBytes.HasValue && existingBytes > request.ExpectedBytes.Value)
        {
            _logger.LogInformation(
                "Existing partial file {FileName} is larger than expected; restarting download.",
                Path.GetFileName(request.DestinationFilePath));

            return 0;
        }

        return existingBytes;
    }

    private static long? ResolveTotalBytes(
        DownloadFileRequest request,
        HttpResponseMessage response,
        long existingBytes,
        bool serverAcceptedResume)
    {
        if (serverAcceptedResume && response.Content.Headers.ContentRange?.Length is long contentRangeLength)
        {
            return contentRangeLength;
        }

        if (request.ExpectedBytes.HasValue)
        {
            return request.ExpectedBytes.Value;
        }

        if (response.Content.Headers.ContentLength is long contentLength)
        {
            return serverAcceptedResume ? existingBytes + contentLength : contentLength;
        }

        return null;
    }

    private static bool ResponseStartsAtExpectedByte(
        HttpResponseMessage response,
        long expectedStartByte)
    {
        return response.Content.Headers.ContentRange?.From == expectedStartByte;
    }

    private async Task<long> CopyToFileAsync(
        Stream responseStream,
        FileStream destinationStream,
        long? totalBytes,
        long bytesDownloaded,
        IProgress<DownloadProgress>? progress,
        PackageDownloadPauseController? pauseController,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(_bufferSize);
        long progressStartTimestamp = _timeProvider.GetTimestamp();
        TimeSpan lastReportElapsed = TimeSpan.Zero;

        try
        {
            using var idleCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

            while (true)
            {
                await PackageDownloadPauseController.WaitWhilePausedAsync(pauseController, cancellationToken)
                    .ConfigureAwait(false);
                idleCancellation.CancelAfter(_idleTimeout);

                int bytesRead;
                try
                {
                    bytesRead = await responseStream
                        .ReadAsync(buffer.AsMemory(0, _bufferSize), idleCancellation.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("Download stalled while waiting for response data.");
                }

                if (bytesRead == 0)
                {
                    break;
                }

                idleCancellation.CancelAfter(Timeout.InfiniteTimeSpan);

                await PackageDownloadPauseController.WaitWhilePausedAsync(pauseController, cancellationToken)
                    .ConfigureAwait(false);
                await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                    .ConfigureAwait(false);

                bytesDownloaded += bytesRead;
                TimeSpan elapsed = _timeProvider.GetElapsedTime(progressStartTimestamp);
                if (elapsed - lastReportElapsed >= _progressReportInterval)
                {
                    ReportProgress(progress, totalBytes, bytesDownloaded);
                    lastReportElapsed = elapsed;
                }
            }

            ReportProgress(progress, totalBytes, bytesDownloaded);
            return bytesDownloaded;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool IsRetriable(Exception exception)
    {
        return exception is HttpRequestException or IOException or TimeoutException or
               TaskCanceledException;
    }

    private TimeSpan GetRetryDelay(int attempt)
    {
        double multiplier = Math.Pow(2, Math.Max(0, attempt - 1));
        double delayMilliseconds = _initialRetryDelay.TotalMilliseconds * multiplier;
        return TimeSpan.FromMilliseconds(Math.Min(delayMilliseconds, TimeSpan.FromSeconds(30).TotalMilliseconds));
    }

    private static void ReportProgress(
        IProgress<DownloadProgress>? progress,
        long? totalBytes,
        long bytesDownloaded)
    {
        double? percentage = null;
        if (totalBytes is > 0)
        {
            percentage = Math.Round((double)bytesDownloaded / totalBytes.Value * 100, 2);
        }

        progress?.Report(new DownloadProgress(totalBytes, bytesDownloaded, percentage));
    }
}
