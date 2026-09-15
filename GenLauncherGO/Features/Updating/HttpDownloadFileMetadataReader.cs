using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Shared.IO;
using GenLauncherGO.Shared.Remote;

namespace GenLauncherGO.Features.Updating;

/// <summary>
///     Reads downloadable file metadata over HTTP.
/// </summary>
internal sealed class HttpDownloadFileMetadataReader : IDownloadFileMetadataReader
{
    private static readonly HttpClient _sharedHttpClient =
        SharedHttpClientFactory.Create(TimeSpan.FromSeconds(60));

    private readonly HttpClient _httpClient;

    public HttpDownloadFileMetadataReader(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? _sharedHttpClient;
    }

    public async Task<DownloadFileMetadata> ReadMetadataAsync(
        Uri downloadUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(downloadUri);

        DownloadFileMetadata? metadata = await HttpHeadFallbackRequest.SendAsync(
            _httpClient,
            downloadUri,
            response => ReadMetadata(downloadUri, response),
            static result => result is null,
            cancellationToken).ConfigureAwait(false);

        return metadata ?? throw new InvalidOperationException(
            "Download link is incorrect, please contact modification creator and try again later.");
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
            NormalizeFileName(fileName),
            response.Content.Headers.ContentLength);
    }

    private static string NormalizeFileName(string fileName)
    {
        try
        {
            return LexicalPath.NormalizePathSegment(fileName.Trim('"'), nameof(fileName));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "Download link is incorrect, please contact modification creator and try again later.",
                exception);
        }
    }
}
