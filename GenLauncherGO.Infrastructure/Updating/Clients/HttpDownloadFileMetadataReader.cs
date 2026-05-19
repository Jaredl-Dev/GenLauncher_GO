using System;
using System.Net;
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
/// Reads downloadable file metadata over HTTP.
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
            DownloadFileMetadata? metadata = await TryReadMetadataAsync(
                downloadUri,
                HttpMethod.Head,
                cancellationToken).ConfigureAwait(false);
            if (metadata is not null)
            {
                return metadata;
            }

            metadata = await TryReadMetadataAsync(
                downloadUri,
                HttpMethod.Get,
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

    private async Task<DownloadFileMetadata?> TryReadMetadataAsync(
        Uri downloadUri,
        HttpMethod httpMethod,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(httpMethod, downloadUri);
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (httpMethod == HttpMethod.Head &&
            (response.StatusCode == HttpStatusCode.MethodNotAllowed ||
             response.StatusCode == HttpStatusCode.NotImplemented))
        {
            return null;
        }

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
        string sanitizedFileName = fileName.Trim('"').Replace("\\", string.Empty).Replace("/", string.Empty);
        if (string.IsNullOrWhiteSpace(sanitizedFileName))
        {
            throw new InvalidOperationException(
                "Download link is incorrect, please contact modification creator and try again later.");
        }

        return sanitizedFileName;
    }
}
