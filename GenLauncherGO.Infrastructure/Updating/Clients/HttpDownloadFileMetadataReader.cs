using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Remote;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using GenLauncherGO.Infrastructure.Updating.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Infrastructure.Updating.Clients;

/// <summary>
///     Reads downloadable file metadata over HTTP.
/// </summary>
internal sealed class HttpDownloadFileMetadataReader : IDownloadFileMetadataReader
{
    private static readonly HttpClient _sharedHttpClient =
        SharedHttpClientFactory.Create(TimeSpan.FromSeconds(60));

    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpDownloadFileMetadataReader> _logger;

    public HttpDownloadFileMetadataReader(
        HttpClient? httpClient = null,
        ILogger<HttpDownloadFileMetadataReader>? logger = null)
    {
        _httpClient = httpClient ?? _sharedHttpClient;
        _logger = logger ?? NullLogger<HttpDownloadFileMetadataReader>.Instance;
    }

    public async Task<DownloadFileMetadata> ReadMetadataAsync(
        Uri downloadUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(downloadUri);

        try
        {
            DownloadFileMetadata? metadata = await HttpHeadFallbackRequest.SendAsync(
                _httpClient,
                downloadUri,
                response => ReadMetadata(downloadUri, response),
                static result => result is null,
                cancellationToken).ConfigureAwait(false);
            if (metadata is not null)
            {
                return metadata;
            }

            _logger.LogWarning(
                "Remote download metadata did not include a file name for {Scheme}://{Host}.",
                downloadUri.Scheme,
                downloadUri.Host);
            throw new InvalidOperationException(
                "Download link is incorrect, please contact modification creator and try again later.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Failed to read remote download metadata from {Scheme}://{Host}; failure type: {FailureType}.",
                downloadUri.Scheme,
                downloadUri.Host,
                exception.GetType().Name);
            throw;
        }
    }

    private static DownloadFileMetadata? ReadMetadata(
        Uri downloadUri,
        HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();

        string? fileName = response.Content.Headers.ContentDisposition?.FileNameStar;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = response.Content.Headers.ContentDisposition?.FileName;
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        return new DownloadFileMetadata(
            downloadUri,
            SanitizeFileName(fileName),
            response.Content.Headers.ContentLength);
    }

    private static string SanitizeFileName(string fileName)
    {
        string sanitizedFileName = fileName.Trim('"').Replace("\\", string.Empty, StringComparison.Ordinal).Replace("/", string.Empty, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(sanitizedFileName))
        {
            throw new InvalidOperationException(
                "Download link is incorrect, please contact modification creator and try again later.");
        }

        return sanitizedFileName;
    }
}
